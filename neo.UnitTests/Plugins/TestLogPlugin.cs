using Neo.Plugins;

namespace Neo.UnitTests.Plugins
{
    public class TestLogPlugin : Plugin, ILogPlugin
    {
        public TestLogPlugin() : base() { }

        public string Output { set; get; }

        public override void Configure() { }

        public new void Log(string source, LogLevel level, string message)
        {
            Output = source + "_" + level.ToString() + "_" + message;
        }

        public void LogMessage(string message)
        {
            Log(message);
        }

        public bool TestOnMessage(object message)
        {
            return OnMessage(message);
        }

        protected override bool OnMessage(object message) => true;
    }
}