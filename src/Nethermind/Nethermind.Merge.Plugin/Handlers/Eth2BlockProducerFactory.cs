//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using Nethermind.Blockchain;
using Nethermind.Blockchain.Comparers;
using Nethermind.Blockchain.Processing;
using Nethermind.Blockchain.Producers;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Rewards;
using Nethermind.Blockchain.Validators;
using Nethermind.Consensus;
using Nethermind.Consensus.Transactions;
using Nethermind.Core.Specs;
using Nethermind.Db;
using Nethermind.Logging;
using Nethermind.State;
using Nethermind.Trie.Pruning;
using Nethermind.TxPool;

namespace Nethermind.Merge.Plugin.Handlers
{
    public class Eth2BlockProducerFactory
    {
        public virtual Eth2BlockProducer Create(IBlockTree blockTree,
            IDbProvider dbProvider, 
            IReadOnlyTrieStore readOnlyTrieStore,
            IBlockPreprocessorStep blockPreprocessor, 
            ITxPool txPool, 
            IBlockValidator blockValidator, 
            IRewardCalculatorSource rewardCalculatorSource,
            IReceiptStorage receiptStorage,
            IBlockProcessingQueue blockProcessingQueue,
            ISpecProvider specProvider,
            ISigner engineSigner,
            IMiningConfig miningConfig,
            ILogManager logManager)
        {
            BlockProducerContext producerContext = GetProducerChain(
                blockTree,
                dbProvider,
                readOnlyTrieStore,
                blockPreprocessor,
                txPool,
                blockValidator, 
                rewardCalculatorSource, 
                receiptStorage,
                specProvider,
                miningConfig,
                logManager);
                
            return new Eth2BlockProducer(
                producerContext.TxSource,
                producerContext.ChainProcessor,
                blockTree,
                blockProcessingQueue,
                producerContext.ReadOnlyStateProvider,
                new TargetAdjustedGasLimitCalculator(specProvider, miningConfig),
                engineSigner,
                specProvider,
                logManager);
        }
        
        protected BlockProducerContext GetProducerChain(IBlockTree blockTree,
            IDbProvider dbProvider,
            IReadOnlyTrieStore readOnlyTrieStore,
            IBlockPreprocessorStep blockPreprocessor,
            ITxPool txPool,
            IBlockValidator blockValidator, 
            IRewardCalculatorSource rewardCalculatorSource, 
            IReceiptStorage receiptStorage,
            ISpecProvider specProvider,
            IMiningConfig miningConfig,
            ILogManager logManager)
        {
            ReadOnlyDbProvider readOnlyDbProvider = dbProvider.AsReadOnly(false);
            ReadOnlyBlockTree readOnlyBlockTree = blockTree.AsReadOnly();

            ReadOnlyTxProcessingEnv txProcessingEnv =
                new(readOnlyDbProvider, readOnlyTrieStore, readOnlyBlockTree, specProvider, logManager);
            ITransactionComparerProvider transactionComparerProvider = new TransactionComparerProvider(specProvider, blockTree);
                
            BlockProcessor blockProcessor =
                CreateBlockProcessor(txProcessingEnv, 
                    specProvider, 
                    blockValidator, 
                    rewardCalculatorSource, 
                    txPool, 
                    receiptStorage, 
                    logManager);

            IBlockchainProcessor blockchainProcessor =
                new BlockchainProcessor(
                    readOnlyBlockTree,
                    blockProcessor,
                    blockPreprocessor,
                    logManager,
                    BlockchainProcessor.Options.NoReceipts);

            OneTimeChainProcessor chainProcessor = new(
                readOnlyDbProvider,
                blockchainProcessor);

            return new BlockProducerContext
            {
                ChainProcessor = chainProcessor,
                ReadOnlyStateProvider = txProcessingEnv.StateProvider,
                TxSource = CreateTxSourceForProducer(txProcessingEnv, txPool, specProvider, logManager, miningConfig, transactionComparerProvider),
                ReadOnlyTxProcessingEnv = txProcessingEnv
            };
        }

        private ITxSource CreateTxSourceForProducer(ReadOnlyTxProcessingEnv processingEnv, ITxPool txPool, ISpecProvider specProvider, ILogManager logManager, IMiningConfig miningConfig, ITransactionComparerProvider transactionComparerProvider) =>
            CreateTxPoolTxSource(processingEnv, txPool, miningConfig, specProvider, transactionComparerProvider, logManager);

        private TxPoolTxSource CreateTxPoolTxSource(ReadOnlyTxProcessingEnv processingEnv, ITxPool txPool, IMiningConfig miningConfig, ISpecProvider specProvider, ITransactionComparerProvider transactionComparerProvider, ILogManager logManager)
        {
            ITxFilterPipeline txSourceFilterPipeline = CreateTxSourceFilter(logManager, specProvider);
            return new TxPoolTxSource(txPool, processingEnv.StateReader, specProvider, transactionComparerProvider, logManager, txSourceFilterPipeline);
        }

        private ITxFilterPipeline CreateTxSourceFilter(ILogManager logManager, ISpecProvider specProvider) =>
               TxFilterPipelineBuilder.CreateStandardFilteringPipeline(logManager, specProvider);

        private BlockProcessor CreateBlockProcessor(
            ReadOnlyTxProcessingEnv readOnlyTxProcessingEnv,
            ISpecProvider specProvider, 
            IBlockValidator blockValidator, 
            IRewardCalculatorSource rewardCalculatorSource, 
            ITxPool txPool, 
            IReceiptStorage receiptStorage, 
            ILogManager logManager) =>
            new(
                specProvider,
                blockValidator,
                rewardCalculatorSource.Get(readOnlyTxProcessingEnv.TransactionProcessor),
                readOnlyTxProcessingEnv.TransactionProcessor,
                readOnlyTxProcessingEnv.StateProvider,
                readOnlyTxProcessingEnv.StorageProvider,
                txPool,
                receiptStorage,
                NullWitnessCollector.Instance,
                logManager);
    }
}
