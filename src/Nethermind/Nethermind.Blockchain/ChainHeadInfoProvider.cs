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

using Nethermind.Blockchain.Find;
using Nethermind.Blockchain.Spec;
using Nethermind.Core.Specs;
using Nethermind.Int256;
using Nethermind.State;
using Nethermind.TxPool;

namespace Nethermind.Blockchain
{
    public class ChainHeadInfoProvider : IChainHeadInfoProvider
    {
        private readonly IBlockFinder _blockFinder;
        
        public ChainHeadInfoProvider(ISpecProvider specProvider, IBlockFinder blockFinder, IStateReader stateReader)
            : this(new ChainHeadSpecProvider(specProvider, blockFinder), blockFinder, new ChainHeadReadOnlyStateProvider(blockFinder, stateReader))
        {
        }
        public ChainHeadInfoProvider(ISpecProvider specProvider, IBlockFinder blockFinder, IReadOnlyStateProvider stateProvider)
            : this(new ChainHeadSpecProvider(specProvider, blockFinder), blockFinder, stateProvider)
        {
        }
        
        public ChainHeadInfoProvider(IChainHeadSpecProvider specProvider, IBlockFinder blockFinder, IReadOnlyStateProvider stateProvider)
        {
            SpecProvider = specProvider;
            ReadOnlyStateProvider = stateProvider;
            _blockFinder = blockFinder;
        }

        public IChainHeadSpecProvider SpecProvider { get; }
        public IReadOnlyStateProvider ReadOnlyStateProvider { get; }

        public UInt256 BaseFee => _blockFinder.Head?.Header.BaseFee ?? UInt256.Zero;
    }
}
