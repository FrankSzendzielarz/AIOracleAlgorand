using AIOracleAlgorand.ContractProxies;
using Algorand;
using Algorand.Algod;
using Algorand.Algod.Model;
using Algorand.Algod.Model.Transactions;
using Algorand.KMD;
using AlgoStudio.Clients;
using Proxies;
using static TorchSharp.torch.nn;

namespace AIOracleAlgorand
{
    internal class Program
    {
        private static DefaultApi algodApiInstance;
        private static Algorand.KMD.Api kmdApi;
        private const string walletName = "unencrypted-default-wallet";
        private static Account creator;
        private static Account user;
        private static Account user2;
        private static ulong deployedApp;


        static async Task Main(string[] args)
        {
            // Make a connection to the Algod node
            SetUpAlgodConnection();

            // Set up accounts based on mnemonics
            await SetUpAccounts();

            // Start our AI Oracle off-chain component as a background task.
            // In reality we would normally deploy this as a separate process, job, Azure Web Job, etc.
            // For demo purposes the AI Oracle off chain component is running as part of this process.
            Task backgroundTask = Task.Factory.StartNew(() => RunServerOracle());

            // Instantiate the on-chain oracle and deploy it. For demo purposes we just make a new oracle every run.
            var oracleSmartContract = new TextClassifierOracle.TextClassifierOracle();
            deployedApp = (await oracleSmartContract.Deploy(creator, algodApiInstance)).Value;

            // Instantiate an async wrapper for calling the oracle via Algorand
            var asyncOracleProxy = new AsyncOracleProxy(algodApiInstance, deployedApp);

            // Make a payment transaction to the oracle that covers fees and a deposit. The deposit will be returned when the job is purged.
            var transParams = await algodApiInstance.TransactionParamsAsync();
            var depositAndFee = PaymentTransaction.GetPaymentTransactionFromNetworkTransactionParameters(user.Address, Address.ForApplication(deployedApp), 1676900, "pay message", transParams);

            // Call the oracle to classify some text. The result will be stored in a box that is returned by the oracle.
            var result = await asyncOracleProxy.ClassifyText(user, 1000, depositAndFee, "I love you.", "");

            // If the oracle server component is running we will not get a timeout and we will have a text result.

        }


        static async Task RunServerOracle()
        {
            TextClassifierOracleProxy oracleProxy = new TextClassifierOracleProxy(algodApiInstance, deployedApp);

            while (true)
            {
                // Use the SDK to get all the current boxes assigned to the oracle.
                var allBoxes = await algodApiInstance.GetApplicationBoxesAsync(deployedApp);
                
                if (allBoxes.Boxes.Count == 0)
                {
                    await Task.Delay(1000);
                } 
                else
                {
                    foreach (var boxName in allBoxes.Boxes)
                    {
                        var box = await algodApiInstance.GetApplicationBoxByNameAsync(deployedApp, $"b64:{Convert.ToBase64String(boxName.Name)}");
                        if (box != null)
                        {
                            // NOTE: In production we would need some better mechanisms to limit the amount of API calls.
                            //       An accumulation of non-purged boxes would eventually incur unnecessary costs to the off-chain component/node.

                            // Get the text from the box
                            var text = System.Text.Encoding.UTF8.GetString(box.Value);

                            if (!text.StartsWith("RESULT"))
                            {
                                // Classify the text
                                SentimentAnalysis.ModelInput sampleData = new SentimentAnalysis.ModelInput()
                                {
                                    SentimentText = text,
                                };

                                var classification = SentimentAnalysis.Predict(sampleData);

                                var sentiment = Convert.ToBoolean(classification.PredictedLabel) ? "Toxic" : "Not Toxic";

                                // Write the result back to the box
                                

                            }




                        }
                    }
                }

            }
        }


        private static void SetUpAlgodConnection()
        {
            //A standard sandbox connection
            var httpClient = HttpClientConfigurator.ConfigureHttpClient(@"http://localhost:4001", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            algodApiInstance = new DefaultApi(httpClient);

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-KMD-API-Token", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            kmdApi = new Api(client);
            kmdApi.BaseUrl = @"http://localhost:4002";

        }

        private static async Task SetUpAccounts()
        {
            var accounts = await getDefaultWallet();

            //get accounts based on the above private keys using the .NET SDK
            creator = accounts[0];
            user = accounts[1];
            user2 = accounts[2];
        }

        private static async Task<List<Account>> getDefaultWallet()
        {
            string handle = await getWalletHandleToken();
            var accs = await kmdApi.ListKeysInWalletAsync(new ListKeysRequest() { Wallet_handle_token = handle });
            if (accs.Addresses.Count < 3) throw new Exception("Sandbox should offer minimum of 3 demo accounts.");

            List<Account> accounts = new List<Account>();
            foreach (var a in accs.Addresses)
            {

                var resp = await kmdApi.ExportKeyAsync(new ExportKeyRequest() { Address = a, Wallet_handle_token = handle, Wallet_password = "" });
                Account account = new Account(resp.Private_key);
                accounts.Add(account);
            }
            return accounts;

        }

        private static async Task<string> getWalletHandleToken()
        {
            var wallets = await kmdApi.ListWalletsAsync(null);
            var wallet = wallets.Wallets.Where(w => w.Name == walletName).FirstOrDefault();
            var handle = await kmdApi.InitWalletHandleTokenAsync(new InitWalletHandleTokenRequest() { Wallet_id = wallet.Id, Wallet_password = "" });
            return handle.Wallet_handle_token;
        }
    }
}