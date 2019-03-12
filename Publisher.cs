using Akka.Actor;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.VM;
using Neo.SmartContract;
using Neo.Network.P2P.Payloads;
using StackExchange.Redis;
using System;

namespace Neo.Plugins
{
    public class Publisher : UntypedActor
    {
        private readonly ConnectionMultiplexer connection;

        public Publisher(IActorRef blockchain, ConnectionMultiplexer connection)
        {
            this.connection = connection;
            blockchain.Tell(new Blockchain.Register());
        }

        protected override void OnReceive(object message)
        {
            if (message is Blockchain.PersistCompleted completed)
            {
                JObject json = new JObject();
                string index = completed.Block.Header.Index.ToString();
                NeoConsensusMonitor.Log("PersistCompleted", LogLevel.Info, index);
            }
        }

        public static Props Props(IActorRef blockchain, ConnectionMultiplexer connection)
        {
            return Akka.Actor.Props.Create(() => new Publisher(blockchain, connection));
        }

    }
}
