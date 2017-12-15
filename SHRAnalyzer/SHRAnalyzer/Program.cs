﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using CommandLine;
using Kusto.Data.Net.Client;

namespace HelloKusto
{
    class Program
    {
        static string inMarketResultsFilePath;
        static string clientRequestIdsFilePath;
        static string issueMapFilePath;
        static bool genericProcess;
        public static bool needDRALogs;
        public static bool useSyncCalls;
        public static ConsoleColor currentForgroundColor;

        static void Main(string[] args)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            currentForgroundColor = Console.ForegroundColor;

            var commandLineOptions = new CommandLineOptions();
            var commandLineParsingState = CommandLine.Parser.Default.ParseArguments(args, commandLineOptions);
            if (commandLineParsingState)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                if (!string.IsNullOrEmpty(commandLineOptions.InputFile))
                {
                    clientRequestIdsFilePath = commandLineOptions.InputFile;
                    Console.WriteLine("Reading ClientRequestIds from file: " + clientRequestIdsFilePath);
                    ClientRequestIdHelper.Initialize(clientRequestIdsFilePath);
                }

                if (!string.IsNullOrEmpty(commandLineOptions.InputList))
                {
                    Console.WriteLine("Reading ClientRequestIds from command line list: " + commandLineOptions.InputList);
                    ClientRequestIdHelper.Initialize(commandLineOptions.InputList.Split(',').ToList());
                }

                if (!string.IsNullOrEmpty(commandLineOptions.OutputFile))
                {
                    inMarketResultsFilePath = commandLineOptions.OutputFile;
                }
                else
                {
                    inMarketResultsFilePath = Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]), "InMarketResultsFilePath" + ".txt");
                }

                if (!string.IsNullOrEmpty(commandLineOptions.IssueMapFile))
                {
                    issueMapFilePath = commandLineOptions.IssueMapFile;
                    IssueHelper.Initialize(issueMapFilePath);
                }
                else
                {
                    issueMapFilePath = Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]), "IssueMapFilePath" + ".txt");
                }
                Console.WriteLine("Reading IssueMaps from file: " + issueMapFilePath);
                IssueHelper.Initialize(issueMapFilePath);

                needDRALogs = commandLineOptions.WithDRALogs;
                useSyncCalls = commandLineOptions.UseAsyncKustoCalls;
            }

            if(args.Length == 0 || args[0] == "/?" || (string.IsNullOrEmpty(commandLineOptions.InputFile) && string.IsNullOrEmpty(commandLineOptions.InputList)) || string.IsNullOrEmpty(inMarketResultsFilePath) || string.IsNullOrEmpty(issueMapFilePath))
            {
                Console.WriteLine(commandLineOptions.GetUsage());
                Console.ForegroundColor = currentForgroundColor;
                return;
            }

            TestHook();

            if (genericProcess)
            {
                GenericAnalysis();
            }
            else
            {
                SpecificAnalysis();
            }

            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Total time taken for analysis: " + Math.Ceiling((double)elapsedMs / 1000) + " seconds");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Analysis is complete and output is generated at: " + inMarketResultsFilePath);
            Console.ForegroundColor = currentForgroundColor;
        }

        public static void TestHook()
        {
        }

        public static void SpecificAnalysis()
        {
            ProcessClientRequestIds();

            using (StreamWriter file = File.CreateText(inMarketResultsFilePath))
            {
                PrintErrorDetailsForClientRequestIds(file);
                PrintClientRequestIdsBySubscription(file);
                PrintClientRequestIdsByIssues(file);
            }
        }

        public static void GenericAnalysis()
        {
            Parallel.ForEach(IssueHelper.IssueList, (issue) =>
            {
                QueryHelper.ProcessClientRequestInfoDetailsForIssue(issue);
            });

            using (StreamWriter file = File.CreateText(inMarketResultsFilePath))
            {

                file.WriteLine("---------------------------------------------------------- ClientRequestIds for issues----------------------------------------------------------");
                file.WriteLine();
                foreach (var issue in IssueHelper.IssueList)
                {
                    var affectedClientRequestIdInfos = ClientRequestIdHelper.GetAffectedClientRequestInfos(issue);
                    file.WriteLine("*********ClientRequestIDs affected by issue: " + issue.Type + ", Bug: " + issue.BugId + ", affected count:" + affectedClientRequestIdInfos.Count);
                    file.WriteLine();
                    foreach (var clientRequestInfo in affectedClientRequestIdInfos)
                    {
                        string clientRequestInfoStatement = clientRequestInfo.Id;
                        file.WriteLine(clientRequestInfoStatement);
                    }
                    file.WriteLine();
                }
            }
        }

        private static void ProcessClientRequestIds()
        {
            var clientRequestIdTotalCount = ClientRequestIdHelper.clientRequestInfoList.Count;
            var clientRequestIdCurrentCount = 1;

            if (useSyncCalls)
            {

                foreach (var clientRequestInfo in ClientRequestIdHelper.clientRequestInfoList)
                {
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.WriteLine("Started submitting SRSData snd SRSOperations query for ClientRequestId: " + clientRequestInfo.Id);
                    QueryHelper.TriggerSRSDataErrorAndOperationAsyncCalls(clientRequestInfo);
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine("Submitted SRSData snd SRSOperations query for ClientRequestId: ( " + clientRequestIdCurrentCount++ + "/" + clientRequestIdTotalCount + " ) ClientRequestId: " + clientRequestInfo.Id);
                }

                clientRequestIdCurrentCount = 1;
                Parallel.ForEach(ClientRequestIdHelper.clientRequestInfoList, (clientRequestInfo) =>
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("Started processing ClientRequestId: " + clientRequestInfo.Id);
                    QueryHelper.FillClientRequestInfoDetailsWithAsyncCalls(clientRequestInfo);
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("Finished processing ( " + clientRequestIdCurrentCount++ + "/" + clientRequestIdTotalCount + " ) ClientRequestId: " + clientRequestInfo.Id);
                });
            }
            else
            {
                Parallel.ForEach(ClientRequestIdHelper.clientRequestInfoList, (clientRequestInfo) =>
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("Started processing ClientRequestId: " + clientRequestInfo.Id);
                    QueryHelper.FillClientRequestInfoDetails(clientRequestInfo);
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("Finished processing ( " + clientRequestIdCurrentCount++ + "/" + clientRequestIdTotalCount + " ) ClientRequestId: " + clientRequestInfo.Id);
                });

            }

        }

        private static void PrintClientRequestIdsByIssues(StreamWriter file)
        {
            file.WriteLine("---------------------------------------------------------- ClientRequestIds for issues----------------------------------------------------------");
            file.WriteLine();
            foreach (var issue in IssueHelper.IssueList)
            {
                var affectedClientRequestIdInfos = ClientRequestIdHelper.GetAffectedClientRequestInfos(issue);
                if (affectedClientRequestIdInfos != null && affectedClientRequestIdInfos.Count > 0)
                {
                    file.WriteLine("*********ClientRequestIDs affected by issue: " + issue.Type + ", Bug: " + issue.BugId + ", Count: " + affectedClientRequestIdInfos.Count);
                    file.WriteLine();
                    foreach (var clientRequestInfo in affectedClientRequestIdInfos)
                    {
                        string clientRequestInfoStatement = string.IsNullOrEmpty(clientRequestInfo.StampName) ? clientRequestInfo.Id : clientRequestInfo.StampName.Split('-').First() + "   " + clientRequestInfo.Id;
                        file.WriteLine(clientRequestInfoStatement);
                    }
                    file.WriteLine();
                }
            }
            file.WriteLine("*********Uncategorised ClientRequestIDs: ");
            foreach (var clientRequestId in ClientRequestIdHelper.clientRequestInfoList)
            {
                var machingIssues = IssueHelper.GetMachingIssues(clientRequestId.ErrorContent.ToString());

                if (machingIssues == null || machingIssues.Count == 0)
                {
                    string clientRequestInfoStatement = string.IsNullOrEmpty(clientRequestId.StampName) ? clientRequestId.Id : clientRequestId.StampName.Split('-').First() + "   " + clientRequestId.Id;
                    file.WriteLine(clientRequestInfoStatement);
                }
            }
        }

        private static void PrintClientRequestIdsBySubscription(StreamWriter file)
        {
            var fullSubscriptionInfoList = ClientRequestIdHelper.clientRequestInfoList.Select(clientrequestId => clientrequestId.SubscriptionInfo);

            var subscriptionInfoList = fullSubscriptionInfoList != null ? fullSubscriptionInfoList.GroupBy(sub => sub.Id).Select(group => group.FirstOrDefault()) : new List<Subscription>();

            file.WriteLine("---------------------------------------------------------- ClientRequestIds for subscriptions----------------------------------------------------------");
            file.WriteLine();
            foreach (var subscriptionInfo in subscriptionInfoList)
            {
                var affectedClientRequestIdInfos = ClientRequestIdHelper.clientRequestInfoList.Where(clientRequestId => clientRequestId.SubscriptionInfo.Id == subscriptionInfo.Id);
                if (affectedClientRequestIdInfos != null && affectedClientRequestIdInfos.ToList().Count > 0)
                {
                    string subscriptionInfoStatement = "SubscriptionId: " + subscriptionInfo.Id + ", SubscriptionName: " + subscriptionInfo.SubscriptionName +
                        ", CustomerName: " + subscriptionInfo.CustomerName + ", BillingType: " + subscriptionInfo.BillingType +
                        ", OfferType: " + subscriptionInfo.OfferType;
                    file.WriteLine("*********ClientRequestIDs affected (Count: " + affectedClientRequestIdInfos.ToList().Count + ") for " + subscriptionInfoStatement);
                    file.WriteLine();
                    foreach (var clientRequestInfo in affectedClientRequestIdInfos.ToList())
                    {
                        var machingIssues = IssueHelper.GetMachingIssues(clientRequestInfo.ErrorContent.ToString());
                        string issueInfoStatement = "";
                        foreach (var issue in machingIssues)
                        {
                            issueInfoStatement += "| Issue:" + issue.Type + ", Bug:" + issue.BugId;
                        }

                        string clientRequestInfoStatement = string.IsNullOrEmpty(clientRequestInfo.StampName) ? clientRequestInfo.Id : clientRequestInfo.StampName.Split('-').First() + "   " + clientRequestInfo.Id;
                        file.WriteLine(clientRequestInfoStatement + issueInfoStatement);
                    }
                    file.WriteLine();
                }
            }
        }

        private static void PrintErrorDetailsForClientRequestIds(StreamWriter file)
        {
            foreach (var clientRequestInfo in ClientRequestIdHelper.clientRequestInfoList)
            {
                file.WriteLine("*************************************************** Error details of ClientRequestID: " + clientRequestInfo.Id + "***************************************************");

                var machingIssues = IssueHelper.GetMachingIssues(clientRequestInfo.ErrorContent.ToString());

                string clientRequestInfoStatement = "ObjectType: " + clientRequestInfo.ObjectType + ", ObjectId: " + clientRequestInfo.ObjectId +
                    ", ScenarioName: " + clientRequestInfo.ScenarioName + ", ReplicationProvider: " + Utility.GetReplicationProviderName(clientRequestInfo.ReplicationProviderId) +
                    ", Region: " + clientRequestInfo.Region + ", ResourceId: " + clientRequestInfo.ResourceId + ", StampName: " + clientRequestInfo.StampName;
                string subscriptionInfoStatement = "SubscriptionId: " + clientRequestInfo.SubscriptionInfo.Id + ", SubscriptionName: " + clientRequestInfo.SubscriptionInfo.SubscriptionName +
                    ", CustomerName: " + clientRequestInfo.SubscriptionInfo.CustomerName + ", BillingType: " + clientRequestInfo.SubscriptionInfo.BillingType +
                    ", OfferType: " + clientRequestInfo.SubscriptionInfo.OfferType;
                file.WriteLine("------- Details:");
                file.WriteLine(clientRequestInfoStatement);
                file.WriteLine(subscriptionInfoStatement);
                file.WriteLine();

                foreach (var issue in machingIssues)
                {
                    file.WriteLine("Issue: " + issue.Type + ", Bug: " + issue.BugId);
                }

                file.WriteLine();
                file.WriteLine();
                file.WriteLine("------- SRSOperationEvent:");
                file.WriteLine();
                foreach (var srsOperationEvent in clientRequestInfo.SRSOperationEvents)
                {
                    file.WriteLine(srsOperationEvent.PreciseTimeStamp + "   " + srsOperationEvent.ServiceActivityId + "     " + srsOperationEvent.State + "   " + srsOperationEvent.SRSOperationName + "   " + srsOperationEvent.ScenarioName);
                }
                file.WriteLine();
                file.WriteLine("------- SRSDataEvents:");
                file.WriteLine();
                file.WriteLine(clientRequestInfo.ErrorContent.ToString());
                file.WriteLine();

                if (clientRequestInfo.DRAContent.Length > 1)
                {
                    file.WriteLine();
                    file.WriteLine("------- DRAEvents:");
                    file.WriteLine();
                    file.WriteLine(clientRequestInfo.DRAContent.ToString());
                    file.WriteLine();
                }

                if (clientRequestInfo.CBEngineTraceMessagesErrorContent.Length > 1)
                {
                    file.WriteLine();
                    file.WriteLine("------- CBEngineTraceMessagesErrorContent:");
                    file.WriteLine();
                    file.WriteLine(clientRequestInfo.CBEngineTraceMessagesErrorContent.ToString());
                    file.WriteLine();
                }

                if (clientRequestInfo.RCMErrorContent.Length > 1)
                {
                    file.WriteLine();
                    file.WriteLine("------- RcmDiagnosticEvent:");
                    file.WriteLine();
                    file.WriteLine(clientRequestInfo.RCMErrorContent.ToString());
                    file.WriteLine();
                }

                if (clientRequestInfo.GatewayErrorContent.Length > 1)
                {
                    file.WriteLine();
                    file.WriteLine("------- GatewayDiagnosticEvent:");
                    file.WriteLine();
                    file.WriteLine(clientRequestInfo.GatewayErrorContent.ToString());
                    file.WriteLine();
                }
            }
        }

        class SubscriptionComparer : IEqualityComparer<Subscription>
        {
            bool IEqualityComparer<Subscription>.Equals(Subscription x, Subscription y)
            {
                if (x?.Id == null || y?.Id == null)
                    return false;

                return (x.Id.Equals(y.Id, StringComparison.OrdinalIgnoreCase));
            }

            int IEqualityComparer<Subscription>.GetHashCode(Subscription obj)
            {
                if (obj == null || obj.Id == null)
                    return 0;

                return obj.Id.GetHashCode();
            }
        }
    }
}
