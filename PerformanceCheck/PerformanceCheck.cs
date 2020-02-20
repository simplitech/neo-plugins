using Akka.Actor;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Neo.Plugins
{
    public class PerformanceCheck : Plugin, IPersistencePlugin
    {
        public override string Name => "PerformanceCheck";

        protected override void Configure()
        {
            Settings.Load(GetConfiguration());
        }

        private delegate void CommitHandler(StoreView snapshot);
        private event CommitHandler OnCommitEvent;

        public void OnCommit(StoreView snapshot)
        {
            OnCommitEvent?.Invoke(snapshot);
        }

        protected override bool OnMessage(object message)
        {
            if (!(message is string[] args)) return false;
            if (args.Length == 0) return false;
            switch (args[0].ToLower())
            {
                case "help":
                    return OnHelpCommand(args);
                case "block":
                    return OnBlockCommand(args);
                case "tx":
                case "transaction":
                    return OnTransactionCommand(args);
                case "check":
                    return OnCheckCommand(args);
            }
            return false;
        }

        /// <summary>
        /// Process "help" command
        /// </summary>
        private bool OnHelpCommand(string[] args)
        {
            if (args.Length < 2)
                return false;

            if (!string.Equals(args[1], Name, StringComparison.OrdinalIgnoreCase))
                return false;

            Console.WriteLine($"{Name} Commands:\n");
            Console.WriteLine("Block Commands:");
            Console.WriteLine("\tblock time <index/hash>");
            Console.WriteLine("\tblock avgtime [1 - 10000]");
            Console.WriteLine("\tblock sync");
            Console.WriteLine("Check Commands:");
            Console.WriteLine("\tcheck disk");
            Console.WriteLine("\tcheck cpu");
            Console.WriteLine("\tcheck memory");
            Console.WriteLine("\tcheck threads");
            Console.WriteLine("Transaction Commands:");
            Console.WriteLine("\ttx size <hash>");
            Console.WriteLine("\ttx avgsize [1 - 10000]");

            return true;
        }

        /// <summary>
        /// Process "block" command
        /// </summary>
        private bool OnBlockCommand(string[] args)
        {
            if (args.Length < 2) return false;
            switch (args[1].ToLower())
            {
                case "avgtime":
                case "averagetime":
                    return OnBlockAverageTimeCommand(args);
                case "time":
                    return OnBlockTimeCommand(args);
                case "sync":
                case "synchronization":
                    return OnBlockSynchronizationCommand();
                default:
                    return false;
            }
        }

        /// <summary>
        /// Process "block avgtime" command
        /// Prints the average time in seconds the latest blocks are active
        /// </summary>
        private bool OnBlockAverageTimeCommand(string[] args)
        {
            if (args.Length > 3)
            {
                return false;
            }
            else
            {
                uint desiredCount = 1000;
                if (args.Length == 3)
                {
                    if (!uint.TryParse(args[2], out desiredCount))
                    {
                        Console.WriteLine("Invalid parameter");
                        return true;
                    }

                    if (desiredCount < 1)
                    {
                        Console.WriteLine("Minimum 1 block");
                        return true;
                    }

                    if (desiredCount > 10000)
                    {
                        Console.WriteLine("Maximum 10000 blocks");
                        return true;
                    }
                }

                using (var snapshot = Blockchain.Singleton.GetSnapshot())
                {
                    var averageInSeconds = snapshot.GetAverageTimePerBlock(desiredCount) / 1000;
                    Console.WriteLine(averageInSeconds.ToString("Average time/block: 0.00 seconds"));
                }

                return true;
            }
        }

        /// <summary>
        /// Process "block time" command
        /// Prints the block time in seconds of the given block index or block hash
        /// </summary>
        private bool OnBlockTimeCommand(string[] args)
        {
            if (args.Length != 3)
            {
                return false;
            }
            else
            {
                string blockId = args[2];
                Block block = null;

                if (UInt256.TryParse(blockId, out var blockHash))
                {
                    block = Blockchain.Singleton.GetBlock(blockHash);
                }
                else if (uint.TryParse(blockId, out var blockIndex))
                {
                    block = Blockchain.Singleton.GetBlock(blockIndex);
                }

                if (block == null)
                {
                    Console.WriteLine("Block not found");
                }
                else
                {
                    ulong time = block.GetTime();

                    Console.WriteLine($"Block Hash: {block.Hash}");
                    Console.WriteLine($"      Index: {block.Index}");
                    Console.WriteLine($"      Time: {time / 1000} seconds");
                }

                return true;
            }
        }

        /// <summary>
        /// Process "block sync" command
        /// Prints the delay in seconds in the synchronization of the blocks in the network
        /// </summary>
        private bool OnBlockSynchronizationCommand()
        {
            var lastBlockRemote = GetMaxRemoteBlockCount();

            if (lastBlockRemote == 0)
            {
                Console.WriteLine("There are no remote nodes to synchronize the local chain");
            }
            else
            {
                var delayInSeconds = GetBlockSynchronizationDelay(true) / 1000.0;
                Console.WriteLine($"Time to synchronize to the last remote block: {delayInSeconds:0.#} sec");
            }

            return true;
        }

        /// <summary>
        /// Calculates the delay in the synchronization of the blocks between the connected nodes
        /// </summary>
        /// <param name="printMessages">
        /// Specifies if the messages should be printed in the console.
        /// </param>
        /// <returns>
        /// If the number of remote nodes is greater than zero, returns the delay in the
        /// synchronization between the local and the remote nodes in milliseconds; otherwise,
        /// returns zero.
        /// </returns>
        private double GetBlockSynchronizationDelay(bool printMessages = false)
        {
            var lastBlockRemote = GetMaxRemoteBlockCount();
            if (lastBlockRemote == 0)
            {
                return 0;
            }

            var lastBlock = Math.Max(lastBlockRemote, Blockchain.Singleton.Height);

            bool showBlock = printMessages;
            DateTime remote = DateTime.Now;
            DateTime local = remote;

            Task monitorRemote = new Task(() =>
            {
                var lastRemoteBlockIndex = WaitPersistedBlock(lastBlock);
                remote = DateTime.Now;
                if (showBlock)
                {
                    showBlock = false;
                    Console.WriteLine($"Updated block index to {lastRemoteBlockIndex}");
                }
            });

            Task monitorLocal = new Task(() =>
            {
                var lastPersistedBlockIndex = WaitRemoteBlock(lastBlock);
                local = DateTime.Now;
                if (showBlock)
                {
                    showBlock = false;
                    Console.WriteLine($"Updated block index to {lastPersistedBlockIndex}");
                }
            });

            if (printMessages)
            {
                Console.WriteLine($"Current block index is {lastBlock}");
                Console.WriteLine("Waiting for the next block...");
            }

            monitorRemote.Start();
            monitorLocal.Start();

            Task.WaitAll(monitorRemote, monitorLocal);

            var delay = remote - local;

            return Math.Abs(delay.TotalMilliseconds);
        }

        /// <summary>
        /// Pauses the current thread until a new block is persisted in the blockchain
        /// </summary>
        /// <param name="blockIndex">
        /// Specifies if the block index to start monitoring.
        /// </param>
        /// <returns>
        /// Returns the index of the persisted block.
        /// </returns>
        private uint WaitPersistedBlock(uint blockIndex)
        {
            var persistedBlockIndex = blockIndex;
            var updatePersistedBlock = new TaskCompletionSource<bool>();

            CommitHandler commit = (snapshot) =>
            {
                if (snapshot.Height > blockIndex)
                {
                    persistedBlockIndex = snapshot.Height;
                    updatePersistedBlock.TrySetResult(true);
                }
            };

            OnCommitEvent += commit;
            updatePersistedBlock.Task.Wait();
            OnCommitEvent -= commit;

            return persistedBlockIndex;
        }

        /// <summary>
        /// Pauses the current thread until a new block is received from remote nodes
        /// </summary>
        /// <param name="blockIndex">
        /// Specifies if the block index to start monitoring.
        /// </param>
        /// <returns>
        /// Returns the index of the received block.
        /// </returns>
        private uint WaitRemoteBlock(uint blockIndex)
        {
            var remoteBlockIndex = blockIndex;
            var updateRemoteBlock = new TaskCompletionSource<bool>();

            var cancel = new CancellationTokenSource();
            Task broadcast = Task.Run(() =>
            {
                while (!cancel.Token.IsCancellationRequested)
                {
                    // receive a PingPayload is what updates RemoteNode LastBlockIndex
                    System.LocalNode.Tell(Message.Create(MessageCommand.Ping, PingPayload.Create(blockIndex)));
                }
            });

            Task remoteBlock = Task.Run(() =>
            {
                while (remoteBlockIndex == blockIndex)
                {
                    remoteBlockIndex = GetMaxRemoteBlockCount();
                }
            });

            remoteBlock.Wait();
            cancel.Cancel();

            return remoteBlockIndex;
        }

        /// <summary>
        /// Gets the block count of the remote node with the highest height
        /// </summary>
        /// <returns>
        /// If the number of remote nodes is greater than zero, returns block count of the
        /// node with the highest height; otherwise returns zero.
        /// </returns>
        private uint GetMaxRemoteBlockCount()
        {
            var remotes = LocalNode.Singleton.GetRemoteNodes();

            uint maxCount = 0;

            foreach (var node in remotes)
            {
                if (node.LastBlockIndex > maxCount)
                {
                    maxCount = node.LastBlockIndex;
                }
            }

            return maxCount;
        }

        /// <summary>
        /// Process "transaction" command
        /// </summary>
        private bool OnTransactionCommand(string[] args)
        {
            if (args.Length < 2) return false;
            switch (args[1].ToLower())
            {
                case "size":
                    return OnTransactionSizeCommand(args);
                case "avgsize":
                case "averagesize":
                    return OnTransactionAverageSizeCommand(args);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Process "transaction size" command
        /// Prints the size of the transaction in bytes identified by its hash
        /// </summary>
        private bool OnTransactionSizeCommand(string[] args)
        {
            if (args.Length != 3)
            {
                return false;
            }
            else
            {
                using (var snapshot = Blockchain.Singleton.GetSnapshot())
                {
                    Transaction tx = null;
                    if (UInt256.TryParse(args[2], out var transactionHash))
                    {
                        tx = snapshot.GetTransaction(transactionHash);
                    }

                    if (tx == null)
                    {
                        Console.WriteLine("Transaction not found");
                    }
                    else
                    {
                        Console.WriteLine($"Transaction Hash: {tx.Hash}");
                        Console.WriteLine($"            Size: {tx.Size} bytes");
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Process "transaction avgsize" command
        /// Prints the average size in bytes of the latest transactions
        /// </summary>
        private bool OnTransactionAverageSizeCommand(string[] args)
        {
            if (args.Length > 3)
            {
                return false;
            }
            else
            {
                uint desiredCount = 1000;
                if (args.Length == 3)
                {
                    if (!uint.TryParse(args[2], out desiredCount))
                    {
                        Console.WriteLine("Invalid parameter");
                        return true;
                    }

                    if (desiredCount < 1)
                    {
                        Console.WriteLine("Minimum 1 transaction");
                        return true;
                    }

                    if (desiredCount > 10000)
                    {
                        Console.WriteLine("Maximum 10000 transactions");
                        return true;
                    }
                }

                var averageInKbytes = GetSizePerTransaction(desiredCount);
                Console.WriteLine(averageInKbytes.ToString("Average size/tx: 0 bytes"));
            }

            return true;
        }

        /// <summary>
        /// Returns the average size of the latest transactions
        /// </summary>
        /// <param name="desiredCount">
        /// The desired number of transactions that should be checked to calculate the average size
        /// </param>
        /// <returns>
        /// Returns the average size per transaction in bytes if the number of analysed transactions
        /// is greater than zero; otherwise, returns 0.0
        /// </returns>
        private double GetSizePerTransaction(uint desiredCount)
        {
            using (var snapshot = Blockchain.Singleton.GetSnapshot())
            {
                var blockHash = snapshot.CurrentBlockHash;
                var countedTxs = 0;

                Block block = snapshot.GetBlock(blockHash);
                int totalsize = 0;

                do
                {
                    foreach (var tx in block.Transactions)
                    {
                        if (tx != null)
                        {
                            totalsize += tx.Size;
                            countedTxs++;

                            if (desiredCount <= countedTxs)
                            {
                                break;
                            }
                        }
                    }

                    block = snapshot.GetBlock(block.PrevHash);
                } while (block != null && desiredCount > countedTxs);

                double averageSize = 0.0;
                if (countedTxs > 0)
                {
                    averageSize = 1.0 * totalsize / countedTxs;
                }

                return averageSize;
            }
        }

        /// <summary>
        /// Process "check" command
        /// </summary>
        private bool OnCheckCommand(string[] args)
        {
            if (args.Length < 2) return false;
            switch (args[1].ToLower())
            {
                case "disk":
                    return OnCheckDiskCommand();
                case "cpu":
                    return OnCheckCPUCommand();
                case "threads":
                case "activethreads":
                    return OnCheckActiveThreadsCommand();
                case "mem":
                case "memory":
                    return OnCheckMemoryCommand();
                default:
                    return false;
            }
        }

        /// <summary>
        /// Process "check cpu" command
        /// Prints each thread CPU usage information every second
        /// </summary>
        private bool OnCheckCPUCommand()
        {
            var cancel = new CancellationTokenSource();

            Task task = Task.Run(async () =>
            {
                var monitor = new CpuUsageMonitor();

                while (!cancel.Token.IsCancellationRequested)
                {
                    try
                    {
                        var total = monitor.CheckAllThreads(true);
                        if (!cancel.Token.IsCancellationRequested)
                        {
                            Console.WriteLine($"Active threads: {monitor.ThreadCount,3}\tTotal CPU usage: {total,8:0.00 %}");
                        }

                        await Task.Delay(1000, cancel.Token);
                    }
                    catch
                    {
                        // if any unexpected exception is thrown, stop the loop and finish the task
                        cancel.Cancel();
                    }
                }
            });
            Console.ReadLine();
            cancel.Cancel();

            return true;
        }

        /// <summary>
        /// Process "check threads" command
        /// Prints the number of active threads in the current process
        /// </summary>
        private bool OnCheckActiveThreadsCommand()
        {
            var current = Process.GetCurrentProcess();

            Console.WriteLine($"Active threads: {current.Threads.Count}");

            return true;
        }

        /// <summary>
        /// Process "check memory" command
        /// Prints the amount of memory allocated for the current process in megabytes
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private bool OnCheckMemoryCommand()
        {
            var current = Process.GetCurrentProcess();
            var memoryInMB = current.PagedMemorySize64 / 1024 / 1024.0;

            Console.WriteLine($"Allocated memory: {memoryInMB:0.00} MB");

            return true;
        }

        /// <summary>
        /// Process "check disk" command
        /// Prints the disk access information
        /// </summary>
        private bool OnCheckDiskCommand()
        {
            var megabyte = 1024;

            var writePerSec = new PerformanceCounter("Process", "IO Write Bytes/sec", "_Total");
            var readPerSec = new PerformanceCounter("Process", "IO Read Bytes/sec", "_Total");

            var cancel = new CancellationTokenSource();
            Task task = Task.Run(async () =>
            {
                while (!cancel.Token.IsCancellationRequested)
                {
                    Console.Clear();
                    string diskWriteUnit = "KB/s";
                    string diskReadUnit = "KB/s";

                    var diskWritePerSec = Convert.ToInt32(writePerSec.NextValue()) / 1024.0;
                    var diskReadPerSec = Convert.ToInt32(readPerSec.NextValue()) / 1024.0;

                    if (diskWritePerSec > megabyte)
                    {
                        diskWritePerSec = diskWritePerSec / 1024;
                        diskWriteUnit = "MB/s";
                    }
                    if (diskReadPerSec > megabyte)
                    {
                        diskReadPerSec = diskReadPerSec / 1024;
                        diskReadUnit = "MB/s";
                    }

                    Console.WriteLine($"Disk write: {diskWritePerSec:0.0#} {diskWriteUnit}");
                    Console.WriteLine($"Disk read:  {diskReadPerSec:0.0#} {diskReadUnit}");
                    await Task.Delay(1000, cancel.Token);
                }
            });
            Console.ReadLine();
            cancel.Cancel();

            return true;
        }
    }
}
