using System;
using Algorand.Algod;
using Algorand.Algod.Model;
using Algorand.Algod.Model.Transactions;
using AlgoStudio;
using Algorand;
using AlgoStudio.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proxies
{

	
	public class TextClassifierOracleProxy : ProxyBase
	{
		
		public TextClassifierOracleProxy(DefaultApi defaultApi, ulong appId) : base(defaultApi, appId) 
		{
		}

		/// <summary>
        /// Call this to reserve a text classification job. The returned id must be passed into the box references of the classify text call subsequently.
        /// </summary>
        /// <param name="payment">A payment to cover the job deposit and fees. MUST BE 1676900 microalgos</param>
        /// <returns>The job id the oracle will process</returns>
		public async Task<byte[]> StartClassificationJob (Account sender, ulong? fee, PaymentTransaction payment,string note, List<BoxRef> boxes)
		{
			var abiHandle = Encoding.UTF8.GetBytes("Start");
			var result = await base.CallApp(new List<Transaction> {payment}, fee, AlgoStudio.Core.OnCompleteType.NoOp, 1000, note, sender,  new List<object> {abiHandle}, null, null,null,boxes);
			return result.First();

		}

		/// <summary>
        /// Call this to use a job id and wait for the oracle to classify the text. 
        /// The result will be stored back into the box for that job.
        /// </summary>
        /// <param name="text">The text to classify</param>
		public async Task ClassifyText (Account sender, ulong? fee, byte[] jobId,string text,string note, List<BoxRef> boxes)
		{
			var abiHandle = Encoding.UTF8.GetBytes("Classify");
			var result = await base.CallApp(null, fee, AlgoStudio.Core.OnCompleteType.NoOp, 1000, note, sender,  new List<object> {abiHandle,jobId,text}, null, null,null,boxes);

		}

		public async Task PurgeJob (Account sender, ulong? fee, byte[] jobId,string note, List<BoxRef> boxes)
		{
			var abiHandle = Encoding.UTF8.GetBytes("Purge");
			var result = await base.CallApp(null, fee, AlgoStudio.Core.OnCompleteType.NoOp, 1000, note, sender,  new List<object> {abiHandle,jobId}, null, null,null,boxes);

		}

		public async Task ReclaimFees (Account sender, ulong? fee,string note, List<BoxRef> boxes)
		{
			var abiHandle = Encoding.UTF8.GetBytes("Reclaim");
			var result = await base.CallApp(null, fee, AlgoStudio.Core.OnCompleteType.NoOp, 1000, note, sender,  new List<object> {abiHandle}, null, null,null,boxes);

		}

		public async Task<ulong> JobIdCounter()
		{
			var key="JobIdCounter";
			var result= await base.GetGlobalUInt(key);
			return (ulong)result;

		}

	}

}
