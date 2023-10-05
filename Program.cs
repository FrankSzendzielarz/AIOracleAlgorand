using AIOracleAlgorand.ContractProxies;
using Algorand;
using Algorand.Algod;
using Algorand.Algod.Model;
using Algorand.Algod.Model.Transactions;
using Algorand.KMD;
using AlgoStudio.Clients;
using Proxies;

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
            Console.WriteLine("Creating connection to Algod.");
            // Make a connection to the Algod node
            SetUpAlgodConnection();

            Console.WriteLine("Obtaining KMD accounts.");
            // Set up accounts based on mnemonics
            await SetUpAccounts();


            Console.WriteLine("Deploying on-chain AI Oracle.");
            // Instantiate the on-chain oracle and deploy it. For demo purposes we just make a new oracle every run.
            var oracleSmartContract = new TextClassifierOracle.TextClassifierOracle();
            deployedApp = (await oracleSmartContract.Deploy(creator, algodApiInstance)).Value;

            //Fund the app the minimum balance. It needs to participate in transactions.
            await creator.FundContract(deployedApp, 100000, algodApiInstance);

            // Start our AI Oracle off-chain component as a background task.
            // In reality we would normally deploy this as a separate process, job, Azure Web Job, etc.
            // For demo purposes the AI Oracle off chain component is running as part of this process.
            Console.WriteLine("Starting off-chain AI Oracle component.");
            Task backgroundTask = Task.Factory.StartNew(() => RunServerOracle());

            Console.WriteLine("Ready for client to request text classification. Press any key to continue...");
            Console.ReadKey();


            // Instantiate an async wrapper for calling the oracle via Algorand
            var asyncOracleProxy = new AsyncOracleProxy(algodApiInstance, deployedApp);

            // Make a payment transaction to the oracle that covers fees and a deposit. The deposit will be returned when the job is purged.
            var transParams = await algodApiInstance.TransactionParamsAsync();
            var depositAndFee = PaymentTransaction.GetPaymentTransactionFromNetworkTransactionParameters(user.Address, Address.ForApplication(deployedApp), 1676900, "pay message", transParams);

            // Call the oracle to classify some text. The result will be stored in a box that is returned by the oracle.
            var result = await asyncOracleProxy.ClassifyText(user, 1000, depositAndFee, "I love you.", "");
            Console.WriteLine($"Oracle returned {result}");


            Console.WriteLine("End of demo. Press any key to exit.");
            Console.ReadKey();

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

                        Box? box = null;
                        try
                        {
                            box = await algodApiInstance.GetApplicationBoxByNameAsync(deployedApp, $"b64:{Convert.ToBase64String(boxName.Name)}");
                        }
                        catch { }
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

                                var sentiment = Convert.ToBoolean(classification.PredictedLabel) ? "RESULT: Toxic" : "RESULT: Not Toxic";

                                // Write the result back to the box

                                await oracleProxy.CompleteJob(creator, 1000, boxName.Name, sentiment, "", new List<BoxRef>() { new BoxRef() { App = 0, Name = boxName.Name } });


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