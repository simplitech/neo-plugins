using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;

namespace Neo.Plugins
{
    public class MonitorNode : UntypedActor
    {
        private uint LastBlockIndex = 0;

        public delegate void NewBlock();
        public event NewBlock NewBlockEvent;

        TaskCompletionSource<bool> NextBlock;
        private static MonitorNode Instance;

        public static MonitorNode StartNew(NeoSystem System)
        {
            System.ActorSystem.ActorOf(Props());
            while (Instance == null) { }
            return Instance;
        }

        public MonitorNode()
        {
            Context.System.EventStream.Subscribe(Self, typeof(Blockchain.PersistCompleted));

            LastBlockIndex = Blockchain.Singleton.Height;
            NewBlockEvent += OnNewBlockEvent;
            
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

        private void OnNewBlockMessage(Block block)
        {
            var index = block.Index;

            if (index > LastBlockIndex)
            {
                LastBlockIndex = index;
                NewBlockEvent();
            }
        }

        public void WaitForBlock()
        {
            NextBlock = new TaskCompletionSource<bool>();
            NextBlock.Task.Wait();
        }

        private void OnNewBlockEvent()
        {
            NextBlock?.TrySetResult(true);
        }

        public static Props Props()
        {
            return Akka.Actor.Props.Create(() => new MonitorNode());
        }

    }
}
