// NeoConsensusMonitor neo-cli plugin by hal0x2328

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using StackExchange.Redis;
using Neo.Ledger;
using Neo.Persistence;
using Neo.Cryptography.ECC;

namespace Neo.Plugins
{
    public class NeoConsensusMonitor : Plugin, ILogPlugin
    {
        public override string Name => "NeoConsensusMonitor";
        private readonly ConnectionMultiplexer connection;
        private List<string> nodes;
        private Dictionary<string,string> nodenames; // public key to node name mapping
        private Dictionary<string,int> nodeblocks; // how many blocks validated by a node
        private Dictionary<string,long> nodetime; // how much time each node spent validating blocks
        private Dictionary<int,string> blocknodes; // which node validated this block
        private long lastblock; // unix timestamp of previous block

        public NeoConsensusMonitor()
        {
            nodeblocks = new Dictionary<string,int>();
            nodetime = new Dictionary<string,long>();
            blocknodes = new Dictionary<int,string>();

            nodenames = new Dictionary<string,string>{
              {"025bdf3f181f53e9696227843950deb72dcd374ded17c057159513c3d0abe20b64", "COZ"},
              {"035e819642a8915a2572f972ddbdbe3042ae6437349295edce9bdc3b8884bbf9a3", "KPN"},
              {"02df48f60e8f3e01c48ff40b9b7f1310d7a8b2a193188befe1c2e3df740e895093", "NF1"},
              {"024c7b7fb6c310fccf1ba33b082519d82964ea93868d676662d4a59ad548df0e7d", "NF2"},
              {"02ca0e27697b9c248f6f16e085fd0061e26f44da85b58ee835c110caa5ec3ba554", "NF3"},
              {"03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c", "NF4"},
              {"03b8d9d5771d8f513aa0869b9cc8d50986403b78c6da36890638c3d46a5adce04a", "NF5"}
            };

            foreach(string n in nodenames.Values)
            {
                nodetime.Add(n, 0);
                nodeblocks.Add(n, 0);
            }

            nodes = GetConsensusNodes();
            lastblock = 0; 
 
            Console.WriteLine($"Connecting to PubSub server at {Settings.Default.RedisHost}:{Settings.Default.RedisPort}");
            this.connection = ConnectionMultiplexer.Connect($"{Settings.Default.RedisHost}:{Settings.Default.RedisPort}");
            if (this.connection == null) {
                Console.WriteLine($"Connection failed!");
            } else {
                Console.WriteLine($"Connected.");
                System.ActorSystem.ActorOf(Publisher.Props(System.Blockchain, this.connection));
                Log("NeoConsensusMonitor Plugin", LogLevel.Info, "Ready");
            }
        }

        public List<string> GetConsensusNodes()
        {
            Snapshot snapshot = Blockchain.Singleton.GetSnapshot();
            var validators = snapshot.GetValidators();
            List<string> ret = new List<string>();
            foreach (ECPoint p in validators)
            {
                ret.Add(p.ToString());
            }
            return ret;
        }

        void ILogPlugin.Log(string source, LogLevel level, string message)
        {
            DateTime foo = DateTime.UtcNow;
            long unixTime = ((DateTimeOffset)foo).ToUnixTimeSeconds();

            //Console.WriteLine($"[{unixTime}] {source} {message}");

            if (source == "ConsensusService")
            {
                if (message.StartsWith("OnPrepareRequestReceived: "))
                {
                    // OnPrepareRequestReceived: height=3475069 view=0 index=3 tx=7
                    Dictionary<string,string> prep = message.Substring(26).Split(' ')
                      .Select(value => value.Split('='))
                      .ToDictionary(pair => pair[0], pair => pair[1]);
                      int index = Int32.Parse(prep["index"]);
                      int height = Int32.Parse(prep["height"]);
                      string node = nodenames[nodes[index]];
                      int view = Int32.Parse(prep["view"]);
                      if (view == 0) 
                      {
                          blocknodes.Add(height, node);
                      } 
                      else
                      {
                          // a view change happened - don't track this block for statistics
                          if (blocknodes.ContainsKey(height))
                              blocknodes.Remove(height);
                      }
                }
            }
            else if (source == "PersistCompleted")
            {
                if (lastblock > 0)
                {
                    long elapsed = unixTime - lastblock;
                    int block = Int32.Parse(message);
                    if (blocknodes.ContainsKey(block))
                    {
                        // discard results less than 15 seconds
                        if (elapsed >= 15)
                        {
                            string speaker = blocknodes[block];
                            nodeblocks[speaker] = nodeblocks[speaker] + 1;
                            nodetime[speaker] = nodetime[speaker] + elapsed;
                            float a = nodetime[speaker] / nodeblocks[speaker];
                            string average = a.ToString("0.00");
                            Console.WriteLine($"{speaker} produced block {block} in {elapsed} seconds, averaging {average} second blocktimes");
                        }                         
                        blocknodes.Remove(block);
                    }
                }
                lastblock = unixTime;
            }
        }

        private bool OnStats()
        {
            foreach(string speaker in nodenames.Values)
            {
                if (nodeblocks[speaker] > 0)
                {
                    float a = nodetime[speaker] / nodeblocks[speaker];
                    string average = a.ToString("0.00");
                    Console.WriteLine($"{speaker}: {nodeblocks[speaker]} blocks processed with an average of {average} seconds per block");
                } else
                {
                    Console.WriteLine($"{speaker}: No blocks processed since neo-cli start");
                }
            }
            return true;
        }

        public override void Configure()
        {
            Console.WriteLine("Loading configuration for NeoConsensusMonitor plugin...");
            Settings.Load(GetConfiguration());
        }

        protected override bool OnMessage(object message)
        {
            if (!(message is string[] args)) return false;
            if (args.Length == 0) return false;
            switch (args[0].ToLower())
            {
                case "stats":
                    return OnStats();
            }
            return false;
        }

    }
}
