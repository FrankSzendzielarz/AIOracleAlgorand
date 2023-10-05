using Algorand.Algod;
using Algorand.Algod.Model;
using Algorand.Algod.Model.Transactions;
using Proxies;
using System.Text;

namespace AIOracleAlgorand.ContractProxies
{
    public class AsyncOracleProxy
    {
        private TextClassifierOracleProxy textClassifierOracleProxy;
        private DefaultApi algod;
        private ulong appId;
        public AsyncOracleProxy(DefaultApi defaultApi, ulong appId)
        {
            textClassifierOracleProxy = new TextClassifierOracleProxy(defaultApi, appId);
            algod = defaultApi;
            this.appId=appId;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="fee"></param>
        /// <param name="payment">Must be 1676900</param>
        /// <param name="text"></param>
        /// <param name="note"></param>
        /// <param name="boxes"></param>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        public async Task<string> ClassifyText(Account sender, ulong? fee, PaymentTransaction payment, string text, string note)
        {


            // start a job and get the id
            byte[] jobId = await textClassifierOracleProxy.StartClassificationJob(sender, fee, payment, note, null);

            // make a box ref and use the job id to ask the oracle to classify the text
            var boxref = new BoxRef() { App = 0, Name = jobId };
            List<BoxRef> boxes= new List<BoxRef>() {  boxref,boxref,boxref,boxref};
            await textClassifierOracleProxy.ClassifyText(sender, fee, jobId, text, note, boxes);

            // wait for the oracle to complete
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                // Set the timeout
                cts.CancelAfter(TimeSpan.FromMilliseconds(60000));  //timeout after 60 seconds for example
                while (!cts.IsCancellationRequested)
                {
                    var res = await algod.GetApplicationBoxesAsync(appId);
                    //get the box text from the oracle app named by our job id:
                    Box box = await algod.GetApplicationBoxByNameAsync(appId, $"b64:{Convert.ToBase64String(jobId)}");
                    var jobText = Encoding.UTF8.GetString(box.Value);

                    if (jobText != text)
                    {
                        // purge the job to return the deposit
                        await textClassifierOracleProxy.PurgeJob(sender, fee, jobId, note, boxes);

                        return jobText;
                    }

                    //pause
                    await Task.Delay(500);
                }

                // If the code reaches this point, it means the timeout has been reached
                throw new TimeoutException("The operation timed out.");
            }

        }

        public async Task PurgeJob(Account sender, ulong? fee, byte[] jobId, string note, List<BoxRef> boxes)
        {
            await textClassifierOracleProxy.PurgeJob(sender, fee, jobId, note, boxes);
        }
    }
}
