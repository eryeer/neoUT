using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO.Json;
using Neo.Wallets.NEP6;
using Neo.Wallets;
using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using Neo.Wallets.SQLite;

namespace Neo.UnitTests.Wallets.NEP6
{
    [TestClass]
    public class UT_NEP6Wallet
    {
        private NEP6Wallet uut;
        private string wPath;
        public static string GetRandomPath()
        {
            string threadName = Thread.CurrentThread.ManagedThreadId.ToString();
            return Path.GetFullPath(string.Format("Wallet_{0}", new Random().Next(1, 1000000).ToString("X8")) + threadName);
        }

        [TestInitialize]
        public void TestSetup()
        {
            JObject wallet = new JObject();
            wallet["name"] = "name";
            wallet["version"] = new System.Version().ToString();
            wallet["scrypt"] = ScryptParameters.Default.ToJson();
            // test minimally scryptparameters parsing here
            ScryptParameters.FromJson(wallet["scrypt"]).Should().NotBeNull();
            ScryptParameters.FromJson(wallet["scrypt"]).N.Should().Be(ScryptParameters.Default.N);
            wallet["accounts"] = new JArray();
            //accounts = ((JArray)wallet["accounts"]).Select(p => NEP6Account.FromJson(p, this)).ToDictionary(p => p.ScriptHash);
            wallet["extra"] = new JObject();
            // check string json
            wallet.ToString().Should().Be("{\"name\":\"name\",\"version\":\"0.0\",\"scrypt\":{\"n\":16384,\"r\":8,\"p\":8},\"accounts\":[],\"extra\":{}}");
            uut = new NEP6Wallet(wallet);
            string path = GetRandomPath();
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            path = Path.Combine(path, "wallet.json");
            File.WriteAllText(path, wallet.ToString());
            string content = File.ReadAllText(path);
            content.Should().Be(wallet.ToString());
            wPath = path;
        }

        [TestCleanup]
        public void TestCleanUp()
        {
            if (File.Exists(wPath)) File.Delete(wPath);
        }

        [TestMethod]
        public void TestConstructorWithPathAndName()
        {
            NEP6Wallet wallet = new NEP6Wallet(wPath);
            Assert.AreEqual("name", wallet.Name);
            Assert.AreEqual(ScryptParameters.Default.ToJson().ToString(), wallet.Scrypt.ToJson().ToString());
            Assert.AreEqual(new System.Version().ToString(), wallet.Version.ToString());
            wallet = new NEP6Wallet("", "test");
            Assert.AreEqual("test", wallet.Name);
            Assert.AreEqual(ScryptParameters.Default.ToJson().ToString(), wallet.Scrypt.ToJson().ToString());
            Assert.AreEqual(Version.Parse("1.0"), wallet.Version);
        }

        [TestMethod]
        public void TestConstructorWithJObject()
        {
            JObject wallet = new JObject();
            wallet["name"] = "test";
            wallet["version"] = Version.Parse("1.0").ToString();
            wallet["scrypt"] = ScryptParameters.Default.ToJson();
            wallet["accounts"] = new JArray();
            wallet["extra"] = new JObject();
            wallet.ToString().Should().Be("{\"name\":\"test\",\"version\":\"1.0\",\"scrypt\":{\"n\":16384,\"r\":8,\"p\":8},\"accounts\":[],\"extra\":{}}");
            NEP6Wallet w = new NEP6Wallet(wallet);
            Assert.AreEqual("test", w.Name);
            Assert.AreEqual(Version.Parse("1.0").ToString(), w.Version.ToString());
        }

        [TestMethod]
        public void TestGetName()
        {
            Assert.AreEqual("name", uut.Name);
        }

        [TestMethod]
        public void TestGetVersion()
        {
            Assert.AreEqual(new System.Version().ToString(), uut.Version.ToString());
        }
        [TestMethod]
        public void TestAddAccount()
        {

        }

        [TestMethod]
        public void TestContains()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            UInt160 scriptHash = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey).ScriptHash;
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(scriptHash);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestCreateAccountWithPrivateKey()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            UInt160 scriptHash = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey).ScriptHash;
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.CreateAccount(privateKey);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestCreateAccountWithKeyPair()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(contract);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(scriptHash);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.CreateAccount(contract, key);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestCreateAccountWithScriptHash()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(scriptHash);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestDecryptKey()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            string nep2key = key.Export("123");
            uut.Unlock("123");
            KeyPair key1 = uut.DecryptKey(nep2key);
            bool result = key1.Equals(key);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestDeleteAccount()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(scriptHash);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(scriptHash);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
        }

        [TestMethod]
        public void TestGetAccount()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.CreateAccount(key.PrivateKey);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
            WalletAccount account = uut.GetAccount(scriptHash);
            Assert.AreEqual(contract.Address, account.Address);
        }

        [TestMethod]
        public void TestGetAccounts()
        {
            Dictionary<string, KeyPair> keys = new Dictionary<string, KeyPair>();
            uut.Unlock("123");
            for (int i = 0; i < 3; i++)
            {
                byte[] privateKey = new byte[32];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(privateKey);
                }
                KeyPair key = new KeyPair(privateKey);
                Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
                keys.Add(contract.Address, key);
                uut.CreateAccount(key.PrivateKey);
            }
            foreach (var account in uut.GetAccounts())
            {
                if (!keys.TryGetValue(account.Address, out KeyPair key))
                {
                    Assert.Fail();
                }
            }
        }

        [TestMethod]
        public void TestImportCert()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            X509Certificate2 cert = new X509Certificate2(privateKey);
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            uut.Import(cert);
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestImportWif()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            string wif = key.Export();
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.Import(wif);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestImportNep2()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            string nep2key = key.Export("123");
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            uut.Import(nep2key, "123");
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(scriptHash);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            JObject wallet = new JObject();
            wallet["name"] = "name";
            wallet["version"] = new System.Version().ToString();
            ScryptParameters sp = new ScryptParameters(16384, 16, 16);
            wallet["scrypt"] = ScryptParameters.Default.ToJson();
            wallet["accounts"] = new JArray();
            wallet["extra"] = new JObject();
            uut = new NEP6Wallet(wallet);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(false, result);
            nep2key = key.Export("123");
            uut.Import(nep2key, "123");
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestLock()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(key.PrivateKey));
            uut.Unlock("123");
            uut.CreateAccount(key.PrivateKey);
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(scriptHash);
            uut.Lock();
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(key.PrivateKey));
        }

        [TestMethod]
        public void TestMigrate()
        {
            string path = GetRandomPath();
            UserWallet uw = UserWallet.Create(path, "123");
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            uw.CreateAccount(key.PrivateKey);
            string npath = Path.Combine(path, "w.json");
            NEP6Wallet nw = NEP6Wallet.Migrate(npath, path, "123");
            bool result = nw.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestSave()
        {
            JObject wallet = new JObject();
            wallet["name"] = "name";
            wallet["version"] = new System.Version().ToString();
            wallet["scrypt"] = ScryptParameters.Default.ToJson();
            wallet["accounts"] = new JArray();
            wallet["extra"] = new JObject();
            File.WriteAllText(wPath, wallet.ToString());
            uut = new NEP6Wallet(wPath);
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            uut.Unlock("123");
            uut.CreateAccount(key.PrivateKey);
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
            uut.Save();
            NEP6Wallet w = new NEP6Wallet(wPath);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestUnlock()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(key.PrivateKey));
            uut.Unlock("123");
            uut.CreateAccount(key.PrivateKey);
            bool result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestVerifyPassword()
        {
            bool result = uut.VerifyPassword("123");
            Assert.AreEqual(true, result);
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            KeyPair key = new KeyPair(privateKey);
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(key.PublicKey);
            UInt160 scriptHash = contract.ScriptHash;
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(key.PrivateKey));
            uut.Unlock("123");
            uut.CreateAccount(key.PrivateKey);
            result = uut.Contains(scriptHash);
            Assert.AreEqual(true, result);
            result = uut.VerifyPassword("1");
            Assert.AreEqual(false, result);
            result = uut.VerifyPassword("123");
            Assert.AreEqual(true, result);
            uut.DeleteAccount(scriptHash);
            Assert.AreEqual(false, uut.Contains(scriptHash));
            uut.CreateAccount(scriptHash);
            result = uut.VerifyPassword("1");
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void Test_NEP6Wallet_Json()
        {
            uut.Name.Should().Be("name");
            uut.Version.Should().Be(new Version());
            uut.Scrypt.Should().NotBeNull();
            uut.Scrypt.N.Should().Be(ScryptParameters.Default.N);
        }
    }
}
