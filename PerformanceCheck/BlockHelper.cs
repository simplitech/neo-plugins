using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;

namespace Neo.Plugins
{
    internal static class BlockHelper
    {
        /// <summary>
        /// Returns the time the given block was active
        /// </summary>
        /// <param name="block">
        /// The given block to verify the time
        /// </param>
        /// <returns>
        /// Returns 0 if <code>block</code> is the current block or is the genesis block or is null;
        /// otherwise, returns the time the block was active in milliseconds
        /// </returns>
        public static ulong GetTime(this Block block)
        {
            ulong time = 0;

            if (block != null && block.Index > 0 && block.Index < Blockchain.Singleton.Height)
            {
                var nextBlock = Blockchain.Singleton.GetBlock(block.Index + 1);

                if (nextBlock != null)
                {
                    time = nextBlock.Timestamp - block.Timestamp;
                }
            }

            return time;
        }

        /// <summary>
        /// Returns the average time the latest blocks are active
        /// </summary>
        /// <param name="desiredCount">
        /// The desired number of blocks that should be checked to calculate the average time
        /// </param>
        /// <returns>
        /// Returns the average time per block in milliseconds if the number of analysed blocks
        /// is greater than zero; otherwise, returns 0.0
        /// </returns>
        public static double GetAverageTimePerBlock(this SnapshotView snapshot, uint desiredCount)
        {
            var firstIndex = Blockchain.GenesisBlock.Index;
            var blockHash = snapshot.CurrentBlockHash;

            var countedBlocks = -1;
            Block block = snapshot.GetBlock(blockHash);
            ulong totaltime = 0;

            do
            {
                totaltime += block.GetTime();
                block = snapshot.GetBlock(block.PrevHash);
                countedBlocks++;
            } while (block != null && block.Index != firstIndex && desiredCount > countedBlocks);

            double averageTime = 0.0;
            if (countedBlocks > 0)
            {
                averageTime = 1.0 * totaltime / countedBlocks;
            }

            return averageTime;
        }
    }
}
