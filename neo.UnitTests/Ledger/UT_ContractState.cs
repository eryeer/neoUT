using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;

namespace Neo.UnitTests.Ledger
{
    [TestClass]
    public class UT_ContractState
    {
        ContractState contract;
        byte[] script = { 0x01 };
        ContractManifest manifest;

        [TestInitialize]
        public void TestSetup()
        {
            manifest = ContractManifest.CreateDefault(UInt160.Parse("0xa400ff00ff00ff00ff00ff00ff00ff00ff00ff01"));
            contract = new ContractState
            {
                Script = script,
                Manifest = manifest
            };
        }

        [TestMethod]
        public void TestGetHasStorage()
        {
            contract.HasStorage.Should().BeFalse();
        }

        [TestMethod]
        public void TestGetPayable()
        {
            contract.Payable.Should().BeFalse();
        }

        [TestMethod]
        public void TestGetScriptHash()
        {
            // _scriptHash == null
            contract.ScriptHash.Should().Be(script.ToScriptHash());
            // _scriptHash != null
            contract.ScriptHash.Should().Be(script.ToScriptHash());
        }

        [TestMethod]
        public void TestToJson()
        {
            JObject json = contract.ToJson();
            json["hash"].ToString().Should().Be("\"0x820944cfdc70976602d71b0091445eedbc661bc5\"");
            json["script"].ToString().Should().Be("\"01\"");
            json["manifest"].ToString().Should().Be(manifest.ToJson().ToString());
        }
    }
}
