/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Blockchain.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Logging;
using Nethermind.Core.Specs;

namespace Nethermind.Blockchain.Synchronization.FastBlocks
{
    public class FromPivotBlockRequestFeed : IBlockRequestFeed
    {
        private readonly ILogger _logger;
        private readonly IBlockTree _blockTree;
        private readonly IEthSyncPeerPool _syncPeerPool;
        private readonly IBlockValidator _blockValidator;

        private ConcurrentDictionary<Keccak, BlockSyncBatch> _headerDependencies = new ConcurrentDictionary<Keccak, BlockSyncBatch>();
        private ConcurrentStack<BlockSyncBatch> _pendingBatches = new ConcurrentStack<BlockSyncBatch>();
        private ConcurrentDictionary<BlockSyncBatch, object> _sentBatches = new ConcurrentDictionary<BlockSyncBatch, object>();

        private object _handlerLock = new object(); // probably no need for handler lock here
        private SyncStats _syncStats;

        public int RequestSize { get; set; } = 256;

        public long PivotNumber { get; set; } = MainNetSpecProvider.Instance.PivotBlockNumber;

        public Keccak PivotHash { get; set; } = MainNetSpecProvider.Instance.PivotBlockHash;

        public long? BestDownwardSyncNumber { get; set; }

        public long? BestDownwardRequestedNumber { get; set; }

        private Keccak NextHash;

        public FromPivotBlockRequestFeed(IBlockTree blockTree, IEthSyncPeerPool syncPeerPool, IBlockValidator blockValidator, ILogManager logManager)
        {
            _logger = logManager?.GetClassLogger() ?? throw new ArgumentNullException(nameof(logManager));
            _blockTree = blockTree ?? throw new ArgumentNullException(nameof(blockTree));
            _syncPeerPool = syncPeerPool ?? throw new ArgumentNullException(nameof(syncPeerPool));
            _blockValidator = blockValidator ?? throw new ArgumentNullException(nameof(blockValidator));
            _syncStats = new SyncStats(logManager);

            _totalHeadersReceived = 0;
            StartNewRound();
        }

        private long _requestsSent = 0;
        private long _itemsSaved = 0;
        private object _empty = new object();

        public BlockSyncBatch PrepareRequest(int threshold)
        {
            if (NextHash == null)
            {
                NextHash = PivotHash;
            }

            BlockSyncBatch blockSyncBatch;
            if (_pendingBatches.Any())
            {
                _pendingBatches.TryPop(out blockSyncBatch);
            }
            else
            {
                if (BestDownwardSyncNumber == 0L || BestDownwardRequestedNumber == 0L)
                {
                    return null;
                }
                
                blockSyncBatch = new BlockSyncBatch();
                blockSyncBatch.MinNumber = BestDownwardRequestedNumber ?? PivotNumber;

                blockSyncBatch.HeadersSyncBatch = new HeadersSyncBatch();
                blockSyncBatch.HeadersSyncBatch.StartNumber = Math.Max(0, (BestDownwardRequestedNumber - 1 ?? PivotNumber) - (RequestSize - 1));
                blockSyncBatch.HeadersSyncBatch.RequestSize = (int)Math.Min(BestDownwardRequestedNumber ?? PivotNumber + 1, RequestSize);
                BestDownwardRequestedNumber = blockSyncBatch.HeadersSyncBatch.StartNumber.Value;
            }

            _sentBatches[blockSyncBatch] = _empty;
            return blockSyncBatch;
        }

        private int _totalHeadersReceived = 0;

        public (BlocksDataHandlerResult Result, int BlocksConsumed) HandleResponse(BlockSyncBatch syncBatch)
        {
            lock (_handlerLock)
            {
                _totalHeadersReceived += syncBatch.HeadersSyncBatch.Response?.Count(r => r != null) ?? 0;
                if (syncBatch.HeadersSyncBatch != null)
                {
                    int added = SuggestBatch(syncBatch);
                    return (BlocksDataHandlerResult.OK, added);
                }

                return (BlocksDataHandlerResult.InvalidFormat, 0);
            }
        }

        public void StartNewRound()
        {
            _pendingBatches.Clear();
            _headerDependencies.Clear();
            _sentBatches.Clear();
        }

        private int SuggestBatch(BlockSyncBatch syncBatch)
        {
            try
            {
                var headersSyncBatch = syncBatch.HeadersSyncBatch;
                if (headersSyncBatch.Response == null)
                {
                    syncBatch.AssignedPeer = null;
                    _pendingBatches.Push(syncBatch);
                    return 0;
                }

                decimal ratio = (decimal) _itemsSaved / (_requestsSent == 0 ? 1 : _requestsSent);
                _requestsSent += syncBatch.HeadersSyncBatch.RequestSize;

                if (_logger.IsDebug) _logger.Debug($"Handling batch {syncBatch}, now best suggested {_blockTree.BestSuggested?.Number ?? 0} | {ratio:p2}");

                int added = 0;
                bool stackRemaining = true;

                for (int i = headersSyncBatch.Response.Length - 1; i >= 0; i--)
                {
                    bool isFirst = i == headersSyncBatch.Response.Length - 1;
                    BlockHeader header = headersSyncBatch.Response[i];
                    if (header == null)
                    {
                        break;
                    }

                    if (isFirst)
                    {
                        // response does not carry expected data
                        if (header.Number == BestDownwardSyncNumber && header.Hash != _blockTree.FindHeader(BestDownwardSyncNumber.Value).Hash)
                        {
                            if (syncBatch.AssignedPeer != null)
                            {
                                _syncPeerPool.ReportInvalid(syncBatch.AssignedPeer);
                            }

                            break;
                        }

                        // response needs to be cached until predecessors arrive
                        if (header.Hash != NextHash)
                        {
                            if (_headerDependencies.ContainsKey(header.Hash))
                            {
                                throw new InvalidOperationException("Only one header dependency expected");
                            }

                            for (int j = 0; j < syncBatch.HeadersSyncBatch.Response.Length; j++)
                            {
                                if (syncBatch.HeadersSyncBatch.Response[i] != null)
                                {
                                    added++;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            
                            BlockSyncBatch dependentBatch = new BlockSyncBatch();
                            dependentBatch.HeadersSyncBatch = new HeadersSyncBatch();
                            dependentBatch.HeadersSyncBatch.StartNumber = syncBatch.HeadersSyncBatch.StartNumber;
                            dependentBatch.HeadersSyncBatch.RequestSize = added;
                            dependentBatch.AssignedPeer = null;
                            dependentBatch.MinNumber = syncBatch.MinNumber;
                            dependentBatch.HeadersSyncBatch.Response = syncBatch.HeadersSyncBatch.Response.Take(added).ToArray(); // perf
                            _headerDependencies[header.Hash] = dependentBatch;

                            // but we cannot do anything with it yet
                            break;
                        }
                    }
                    else
                    {
                        if (header.Hash != headersSyncBatch.Response[i + 1].ParentHash)
                        {
                            if (syncBatch.AssignedPeer != null)
                            {
                                _syncPeerPool.ReportInvalid(syncBatch.AssignedPeer);
                            }

                            break;
                        }
                    }

                    _syncStats.ReportBlocksDownload(PivotNumber - (BestDownwardSyncNumber ?? PivotNumber), PivotNumber, PivotNumber, ratio);


                    // validate hash only
                    bool isValid = true;

                    // override unknown parent here
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    AddBlockResult addBlockResult = isValid ? SuggestHeader(header) : AddBlockResult.InvalidBlock;

                    added++;

                    if (addBlockResult == AddBlockResult.InvalidBlock)
                    {
                        if (syncBatch.AssignedPeer != null)
                        {
                            _syncPeerPool.ReportBadPeer(syncBatch.AssignedPeer);
                        }

                        break;
                    }
                }

                if (BestDownwardSyncNumber == 0)
                {
                    return added;
                }

                if (added < syncBatch.HeadersSyncBatch.RequestSize && stackRemaining)
                {
                    BlockSyncBatch fixedSyncBatch = new BlockSyncBatch();
                    fixedSyncBatch.HeadersSyncBatch = new HeadersSyncBatch();
                    fixedSyncBatch.HeadersSyncBatch.StartNumber = syncBatch.HeadersSyncBatch.StartNumber.GetValueOrDefault() + added;
                    fixedSyncBatch.HeadersSyncBatch.RequestSize = syncBatch.HeadersSyncBatch.RequestSize - added;
                    fixedSyncBatch.AssignedPeer = null;
                    fixedSyncBatch.MinNumber = syncBatch.MinNumber;
                    _pendingBatches.Push(fixedSyncBatch);
                }

                // if not stackRemaining then it was just held for later
                if (added == 0 && stackRemaining)
                {
                    if (syncBatch.AssignedPeer != null)
                    {
                        _syncPeerPool.ReportNoSyncProgress(syncBatch.AssignedPeer);
                    }
                }
                else
                {
                    // if it was just held for later then let us not report any issues with it
                    added = syncBatch.HeadersSyncBatch.RequestSize;
                }

                return added;
            }
            finally
            {
                _sentBatches.Remove(syncBatch, out _);
            }
        }

        private AddBlockResult SuggestHeader(BlockHeader header)
        {
            if (header.IsGenesis)
            {
                return AddBlockResult.AlreadyKnown;
            }

            AddBlockResult addBlockResult = _blockTree.Insert(header);
            if (addBlockResult == AddBlockResult.Added || addBlockResult == AddBlockResult.AlreadyKnown)
            {
                BestDownwardSyncNumber = Math.Min(BestDownwardSyncNumber ?? PivotNumber, header.Number);
                NextHash = header.ParentHash;
                if (addBlockResult == AddBlockResult.Added)
                {
                    _itemsSaved++;
                }
            }
            
            if (addBlockResult == AddBlockResult.InvalidBlock)
            {
                return addBlockResult;
            }

            if (addBlockResult == AddBlockResult.UnknownParent)
            {
                return addBlockResult;
            }

            if (_headerDependencies.ContainsKey(header.ParentHash))
            {
                BlockSyncBatch batch = _headerDependencies[header.ParentHash];
                {
                    batch.AssignedPeer = null;
                    _headerDependencies.Remove(header.ParentHash, out _);
                    SuggestBatch(batch);
                }
            }

            return addBlockResult;
        }
    }
}