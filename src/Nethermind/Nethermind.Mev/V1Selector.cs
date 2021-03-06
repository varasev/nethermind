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

using System.Collections.Generic;
using System.Linq;
using Nethermind.Core;

namespace Nethermind.Mev
{
    public class V1Selector : IBundleSource
    {
        private readonly IBundleSource _bundleSource;
        private readonly IBundleSimulator _bundleSimulator;

        public V1Selector(IBundleSource bundleSource, IBundleSimulator bundleSimulator)
        {
            _bundleSource = bundleSource;
            _bundleSimulator = bundleSimulator;
        }
        
        public IEnumerable<MevBundle> GetBundles(BlockHeader parent, long gasLimit)
        {
            MevBundle? bestBundle = null;
            long bestAdjustedGasPrice = 0;
            foreach (var bundle in _bundleSource.GetBundles(parent, gasLimit))
            {
                SimulatedMevBundle simulatedMevBundle = _bundleSimulator.Simulate(parent, gasLimit, bundle);
                if (simulatedMevBundle.AdjustedGasPrice > bestAdjustedGasPrice)
                {
                    bestBundle = bundle;
                }
            }

            if (bestBundle is null)
            {
                return Enumerable.Empty<MevBundle>();
            }

            return Enumerable.Repeat(bestBundle, 1);
        }
    }
}
