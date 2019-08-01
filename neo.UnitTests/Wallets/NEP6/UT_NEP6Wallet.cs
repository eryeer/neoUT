using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO.Json;
using Neo.Wallets;
using Neo.Wallets.NEP6;
using Neo.Wallets.SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Neo.UnitTests.Wallets.NEP6
{
    [TestClass]
    public class UT_NEP6Wallet
    {
        private NEP6Wallet uut;

        private static string wPath;

        private static KeyPair keyPair;

        private static UInt160 testScriptHash;

        private string certStr = "308203E2020103308203A806092A864886F70D010701A082039904820395308203913082028706092A864886F70D010706A0820278308202740201003082026D06092A864886F70D010701301C060A2A864886F70D010C0106300E0408D9FF4FB9CD49FAC6020208008082024045F838CEAEDC05ABD69513F1D74E0FEFFD30A0088B36D347F6A95B3AF69D9D0189D64616337149A8A986D6CA3A3CEEDF4B47F0EE8FD1DB75AD9F87E795B9567B7C3FB4AD9C7BB7BF3ED926E1C94ED49D5E19798B884421574AB5E16A4D8ECF326290AC64F21B50C34D544E00A820FF479FC9D2BF0751BDE87AAA0876E053BA4F38BD9CD264B1B0CBFE0AA2C3F0925ECF2CC9C8EF0425CB784BE0D79A3F92A5FDD3BF3AA66E8B7577D0948310B3285BBD2BFE75809EDCAECB4E61A4D4319BBE2B407B70A361711001997B8A15E57CC650523D8E6A719C2317498404E691A45D55381E3F37F2CB16F99B9D4078903F08717986EE07F20152BE96897A53C42AAB4B5CD4AA2D6FAB444EA702D2C105B1B2D040F35F899226043D8DA4791FECC2A42D787C27F8945C99DF5F08353556256B26521DED34ADBA5548722DF1AFD1B6B985295B0328F780218B529C60C52E209F233A67D037E670127F9B7C2FC8AC8747D414106B0CABC7D74DC4238DAF4EA8F8DFDD5412C32A330AAF0D1B6CEAC4003E484FFDA7301406F93EBEA38B211B9FDB92FC6BED5FAF8196E6973B2CED5F31012A801682C7D63B66E5DF246CE5183BD08F53AB9F8B230E808B9EEF235F1C2F303CDA0D689339D94C23ACEAACD0EE86AC6A2C0E3D532F72A857D60BD50DE7D5C381D2CA3F958DD326D7AE5B238BDEA1208D90C91127E64F65A3CC6F0648B773ABA9102025515146109E440321EEE5D3B68994B8D78F3B7F754CB523275CE143D188274B7B3669322A317C91B7272805025ED323E612F774F8258A6C60E59F4EC2773082010206092A864886F70D010701A081F40481F13081EE3081EB060B2A864886F70D010C0A0102A081B43081B1301C060A2A864886F70D010C0103300E040830327C73275ED13002020800048190F7A6310264AF7F0017D1255EE7FAED549845CCBBD4922A92AFACB12980FCEC7E2E883DB5B1821D2321D368A11C71ACAE682CB8C22E795236EEFB3C849C2E5810D9C4DA2B49B4DACD6D5AA7023163C378A294C8A5EC874B0F9D9A259048AE082E79A6742BC9896AFC7FE9D5FE869C3882C50C4E52D8F2A54BD9045B7DF5719769E8526F7732F54D6E0A23AAA9BBA10F0D3125302306092A864886F70D01091531160414B51B416BF3C1260DB32A57FAD419A6CFEA21222830313021300906052B0E03021A05000414AC4B5ECE874BF64A92045245DE3F87E5E7F4298904089F406EB2D0E9F7C402020800";

        public static string GetRandomPath()
        {
            string threadName = Thread.CurrentThread.ManagedThreadId.ToString();
            return Path.GetFullPath(string.Format("Wallet_{0}", new Random().Next(1, 1000000).ToString("X8")) + threadName);
        }

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            keyPair = new KeyPair(privateKey);
            testScriptHash = Neo.SmartContract.Contract.CreateSignatureContract(keyPair.PublicKey).ScriptHash;
        }

        private NEP6Wallet CreateWallet()
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
            NEP6Wallet w = new NEP6Wallet(wallet);
            return w;
        }

        private string CreateWalletFile()
        {
            string path = GetRandomPath();
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            path = Path.Combine(path, "wallet.json");
            File.WriteAllText(path, "{\"name\":\"name\",\"version\":\"0.0\",\"scrypt\":{\"n\":16384,\"r\":8,\"p\":8},\"accounts\":[],\"extra\":{}}");
            return path;
        }

        [TestInitialize]
        public void TestSetup()
        {
            uut = CreateWallet();
            wPath = CreateWalletFile();
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
        public void TestContains()
        {
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(testScriptHash);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestCreateAccountWithPrivateKey()
        {

            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.CreateAccount(keyPair.PrivateKey);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestCreateAccountWithKeyPair()
        {
            Neo.SmartContract.Contract contract = Neo.SmartContract.Contract.CreateSignatureContract(keyPair.PublicKey);
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(contract);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(testScriptHash);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.CreateAccount(contract, keyPair);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestCreateAccountWithScriptHash()
        {
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(testScriptHash);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestDecryptKey()
        {
            string nep2key = keyPair.Export("123");
            uut.Unlock("123");
            KeyPair key1 = uut.DecryptKey(nep2key);
            bool result = key1.Equals(keyPair);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestDeleteAccount()
        {
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.CreateAccount(testScriptHash);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(testScriptHash);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
        }

        [TestMethod]
        public void TestGetAccount()
        {
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.CreateAccount(keyPair.PrivateKey);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
            WalletAccount account = uut.GetAccount(testScriptHash);
            Assert.AreEqual(Neo.SmartContract.Contract.CreateSignatureContract(keyPair.PublicKey).Address, account.Address);
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
            byte[] data = certStr.HexToBytes();
            X509Certificate2 cert = new X509Certificate2(data, "1234");
            Assert.IsNotNull(cert);
            Assert.AreEqual(true, cert.HasPrivateKey);
            uut.Unlock("123");
            WalletAccount account = uut.Import(cert);
            Assert.IsNotNull(account);
        }

        [TestMethod]
        public void TestImportWif()
        {
            string wif = keyPair.Export();
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.Unlock("123");
            uut.Import(wif);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestImportNep2()
        {
            string nep2key = keyPair.Export("123");
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            uut.Import(nep2key, "123");
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(testScriptHash);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            JObject wallet = new JObject();
            wallet["name"] = "name";
            wallet["version"] = new System.Version().ToString();
            ScryptParameters sp = new ScryptParameters(16384, 16, 16);
            wallet["scrypt"] = ScryptParameters.Default.ToJson();
            wallet["accounts"] = new JArray();
            wallet["extra"] = new JObject();
            uut = new NEP6Wallet(wallet);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(false, result);
            nep2key = keyPair.Export("123");
            uut.Import(nep2key, "123");
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestLock()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(keyPair.PrivateKey));
            uut.Unlock("123");
            uut.CreateAccount(keyPair.PrivateKey);
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
            uut.DeleteAccount(testScriptHash);
            uut.Lock();
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(keyPair.PrivateKey));
        }

        [TestMethod]
        public void TestMigrate()
        {
            string path = GetRandomPath();
            UserWallet uw = UserWallet.Create(path, "123");
            uw.CreateAccount(keyPair.PrivateKey);
            string npath = Path.Combine(path, "w.json");
            NEP6Wallet nw = NEP6Wallet.Migrate(npath, path, "123");
            bool result = nw.Contains(testScriptHash);
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
            uut.Unlock("123");
            uut.CreateAccount(keyPair.PrivateKey);
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
            uut.Save();
            NEP6Wallet w = new NEP6Wallet(wPath);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestUnlock()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(keyPair.PrivateKey));
            uut.Unlock("123");
            uut.CreateAccount(keyPair.PrivateKey);
            bool result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestVerifyPassword()
        {
            bool result = uut.VerifyPassword("123");
            Assert.AreEqual(true, result);
            Assert.ThrowsException<System.ArgumentNullException>(() => uut.CreateAccount(keyPair.PrivateKey));
            uut.Unlock("123");
            uut.CreateAccount(keyPair.PrivateKey);
            result = uut.Contains(testScriptHash);
            Assert.AreEqual(true, result);
            result = uut.VerifyPassword("1");
            Assert.AreEqual(false, result);
            result = uut.VerifyPassword("123");
            Assert.AreEqual(true, result);
            uut.DeleteAccount(testScriptHash);
            Assert.AreEqual(false, uut.Contains(testScriptHash));
            uut.CreateAccount(testScriptHash);
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
