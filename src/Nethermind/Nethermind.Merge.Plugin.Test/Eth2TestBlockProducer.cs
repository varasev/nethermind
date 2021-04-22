﻿//  Copyright (c) 2021 Demerzel Solutions Limited
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

using System;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Processing;
using Nethermind.Consensus;
using Nethermind.Consensus.Transactions;
using Nethermind.Core;
using Nethermind.Core.Test.Blockchain;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.State;

namespace Nethermind.Merge.Plugin.Test
{
    internal class Eth2TestBlockProducer : Eth2BlockProducer, ITestBlockProducer
    {
        public Eth2TestBlockProducer(
            ITxSource txSource, 
            IBlockchainProcessor processor, 
            IBlockTree blockTree, 
            IBlockProcessingQueue blockProcessingQueue, 
            IStateProvider stateProvider, 
            IGasLimitCalculator gasLimitCalculator, 
            ISigner signer, ILogManager logManager) 
            : base(txSource, processor, blockTree, blockProcessingQueue, stateProvider, gasLimitCalculator, signer, logManager)
        {
        }

        protected override Block? BlockProduced 
        {
            get => base.BlockProduced;
            set
            {
                base.BlockProduced = value;
                if (value != null)
                {
                    LastProducedBlockChanged?.Invoke(this, new BlockEventArgs(value));
                }
            }
        }

        public Block LastProducedBlock => BlockProduced!;

        public event EventHandler<BlockEventArgs> LastProducedBlockChanged = null!;
        
        public void BuildNewBlock()
        {
            throw new NotSupportedException();
        }
    }
}
