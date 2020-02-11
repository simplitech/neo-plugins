using System;
using System.Threading.Tasks;
using Akka.Actor;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;

namespace Neo.Plugins
{
    public class NodeMonitor : UntypedActor
    {
        /// <summary>
        /// Index of the latest block received from blockchain
        /// </summary>
        public uint LastPersistedBlockIndex { get; private set; }
        /// <summary>
        /// Index of the latest block received from local node
        /// </summary>
        public uint LastRemoteBlockIndex { get; private set; }

        private delegate void NewBlock();
        /// <summary>
        /// An event raised when a new block is received
        /// </summary>
        private event NewBlock NewBlockEvent;

        private TaskCompletionSource<bool> UpdatePersistedBlock;
        private TaskCompletionSource<bool> UpdateLocalBlock;
        private static NodeMonitor Instance;

        /// <summary>
        /// Creates a new instance of MonitorNode.
        /// </summary>
        /// <param name="System">
        /// The parent system of the created MonitorNode.
        /// </param>
        /// <param name="blockHeight">
        /// The block index to start monitoring.
        /// </param>
        /// <returns>
        /// The created instance of MonitorNode
        /// </returns>
        public static NodeMonitor StartNew(NeoSystem System, uint blockHeight = 0)
        {
            System.ActorSystem.ActorOf(Props(blockHeight));
            while (Instance == null) { }
            return Instance;
        }

        /// <summary>
        /// Constructor for initialization of <see cref="NodeMonitor"/>.
        /// <para></para>
        /// Don't use this constructor explicitly. Use <see cref="StartNew(NeoSystem, uint)"/> instead.
        /// </summary>
        /// <param name="blockHeight">
        /// The block index to start monitoring.
        /// </param>
        /// <exception cref="ActorInitializationException">
        /// Akka Actors must be initialized with the <see cref="Akka.Actor.Props"/>
        /// </exception>
        private NodeMonitor(uint blockHeight)
        {
            LastPersistedBlockIndex = blockHeight;
            LastRemoteBlockIndex = blockHeight;

            NewBlockEvent += OnNewBlockEvent;
            LocalNode.Singleton.NewRemoteBlockEvent += OnUpdatedBlockEvent;

            // monitor when a new block is persisted in the blockchain
            Context.System.EventStream.Subscribe(Self, typeof(Blockchain.PersistCompleted));

            Instance = this;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Blockchain.PersistCompleted completed:
                    OnNewBlockMessage(completed.Block);
                    break;
            }
        }

        /// <summary>
        /// Process the received block.
        /// If the block is newer than the current latest, invokes <see cref="NewBlockEvent"/>
        /// </summary>
        /// <param name="block">
        /// The received block.
        /// </param>
        private void OnNewBlockMessage(Block block)
        {
            var index = block.Index;

            if (index > LastPersistedBlockIndex)
            {
                LastPersistedBlockIndex = index;
                NewBlockEvent();
            }
        }

        /// <summary>
        /// Pauses the current thread until a new block is received from blockchain
        /// </summary>
        public void WaitPersistedBlock()
        {
            UpdatePersistedBlock = new TaskCompletionSource<bool>();
            UpdatePersistedBlock.Task.Wait();
        }

        /// <summary>
        /// Pauses the current thread until a new block is received from remote nodes
        /// </summary>
        public void WaitRemoteBlock()
        {
            UpdateLocalBlock = new TaskCompletionSource<bool>();
            UpdateLocalBlock.Task.Wait();
        }

        /// <summary>
        /// Finishes the <see cref="UpdatePersistedBlock"/> task
        /// </summary>
        private void OnNewBlockEvent()
        {
            UpdatePersistedBlock?.TrySetResult(true);
        }

        /// <summary>
        /// Finishes the <see cref="UpdateLocalBlock"/> task
        /// </summary>
        /// <param name="lastBlockIndex">
        /// Index of the new block received from a remote node
        /// </param>
        private void OnUpdatedBlockEvent(uint lastBlockIndex)
        {
            if (lastBlockIndex > LastRemoteBlockIndex)
            {
                LastRemoteBlockIndex = lastBlockIndex;
                UpdateLocalBlock?.TrySetResult(true);
            }
        }

        /// <summary>
        /// Gets the configuration object of <see cref="NodeMonitor"/> actor
        /// </summary>
        /// <param name="blockHeight">
        /// The block index to start monitoring.
        /// </param>
        /// <returns>
        /// Returns the configuration object
        /// </returns>
        public static Props Props(uint blockHeight)
        {
            return Akka.Actor.Props.Create(() => new NodeMonitor(blockHeight));
        }

    }
}
