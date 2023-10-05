using AlgoStudio.Core;
using AlgoStudio.Core.Attributes;

namespace AIOracleAlgorand.Contracts
{
    public class TextClassifierOracle : SmartContract
    {
        [Storage(StorageType.Global)]
        public ulong JobIdCounter;



        protected override int ApprovalProgram(in AppCallTransactionReference transaction)
        {

            // Defer to router
            InvokeSmartContractMethod();

            //check if this is a creation and just succeed
            if (transaction.ApplicationID == 0)
            {
                return 1;
            }

            //Check if this is a deletion request
            if (transaction.OnCompletion == 5)
            {
                // Check the deletion is being executed by the creator. 
                AccountReference deleter = transaction.Sender;
                byte[] creatorAddress = CreatorAddress;
                AccountReference creator = creatorAddress.ToAccountReference();
                if (deleter == creator)
                {
                    return 1;
                }
            }

            //Default fail
            return 0;
        }

        protected override int ClearStateProgram(in AppCallTransactionReference transaction)
        {
            return 1;
        }

        /// <summary>
        /// Call this to reserve a text classification job. The returned id must be passed into the box references of the classify text call subsequently.
        /// </summary>
        /// <param name="payment">A payment to cover the job deposit and fees. MUST BE 1676900 microalgos</param>
        /// <returns>The job id the oracle will process</returns>
        [SmartContractMethod(OnCompleteType.NoOp, "Start")]
        public byte[] StartClassificationJob(PaymentTransactionReference payment, AppCallTransactionReference current)
        {
            string pay = "pay";

            if (payment.TxType == pay.ToByteArray() &&
                 payment.Receiver == CurrentApplicationAddress &&
                 payment.Amount == 1676900  // 10000 microAlgos fee plus an MBR deposit
                 )
            {
                // read from the global variable
                ulong jobIdCounter = JobIdCounter;

                // cast it to bytes
                byte[] jobCounter = jobIdCounter.ToTealBytes();

                // get the sender
                AccountReference senderAccount = current.Sender;
                byte[] senderAddress = senderAccount.Address();

                // make a unique job id for the sender
                byte[] jobId = senderAddress.Concat(jobCounter);



                // increment the job id counter
                JobIdCounter = JobIdCounter + 1;

                return jobId;
            }

            byte[] noJob = { };
            return noJob;
        }



        /// <summary>
        /// Call this to use a job id and wait for the oracle to classify the text. 
        /// The result will be stored back into the box for that job.
        /// </summary>
        /// <param name="text">The text to classify</param>
        
        [SmartContractMethod(OnCompleteType.NoOp, "Classify")]
        public void ClassifyText(byte[] jobId, string text, AppCallTransactionReference current)
        {
            // verify the job caller can use that id
            AccountReference senderAccount = current.Sender;
            byte[] senderAddress = senderAccount.Address();
            byte[] jobIdAddress=jobId.Part(0, 32);
            if (jobIdAddress==senderAddress)
            {
                BoxSet(jobId, text.ToByteArray());
            }


        }

        /// <summary>
        /// The contract creator (the offchain oracle) can update the job with results.
        /// </summary>
        /// <param name="jobId">job to update</param>
        /// <param name="text">test classification</param>
        [SmartContractMethod(OnCompleteType.NoOp, "Complete")]
        public void CompleteJob(byte[] jobId, string text, AppCallTransactionReference current)
        {
            // verify the caller is the creator
            AccountReference senderAccount = current.Sender;
            byte[] senderAddress = senderAccount.Address();
            if (senderAddress == CreatorAddress)
            {
                BoxSet(jobId, text.ToByteArray());
            }


        }

        /// <summary>
        /// The user can reclaim their deposit if the job is no longer needed.
        /// </summary>
        /// <param name="jobId">Job to purge.</param>
        [SmartContractMethod(OnCompleteType.NoOp, "Purge")]
        public void PurgeJob(byte[] jobId, AppCallTransactionReference current)
        {
            // verify the job caller can use that id
            AccountReference senderAccount = current.Sender;
            byte[] senderAddress = senderAccount.Address();
            byte[] jobIdAddress = jobId.Part(0, 32);
            if (jobIdAddress == senderAddress)
            {
                // delete the box
                BoxDel(jobId);

                [InnerTransactionCall]
                void PayCaller()
                {
                    new Payment(current.Sender, 1666900);
                }

                // pay the caller their deposit back
                PayCaller();
            }
        }

        /// <summary>
        /// The creator (offchain oracle) can periodically claim accumulated fees.
        /// </summary>
        [SmartContractMethod(OnCompleteType.NoOp, "Reclaim")]
        public void ReclaimFees()
        {
            [InnerTransactionCall]
            void PayCreator()
            {
                byte[] creatorAddress = CreatorAddress;
                AccountReference creator = creatorAddress.ToAccountReference();
                new Payment(creator, Balance - MinBalance);
            }

            if (Balance > MinBalance)
            {
                PayCreator();
            }
        }




    }
}
