using Microsoft.WindowsAzure.MediaServices.Client;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FaceRedaction
{
    class Program
    {
        // Read values from the App.config file.
        private static readonly string _AADTenantDomain =
            ConfigurationManager.AppSettings["AMSAADTenantDomain"];
        private static readonly string _RESTAPIEndpoint =
            ConfigurationManager.AppSettings["AMSRESTAPIEndpoint"];
        private static readonly string _AMSClientId =
            ConfigurationManager.AppSettings["AMSClientId"];
        private static readonly string _AMSClientSecret =
            ConfigurationManager.AppSettings["AMSClientSecret"];

        // Field for service context.
        private static CloudMediaContext _context = null;

        //Arguments usage: [redact/detect] [existing uid/new video file name]
        static void Main(string[] args)
        {
            if (args.Length == 2)
            {
                AzureAdTokenCredentials tokenCredentials =
                    new AzureAdTokenCredentials(_AADTenantDomain,
                        new AzureAdClientSymmetricKey(_AMSClientId, _AMSClientSecret),
                        AzureEnvironments.AzureCloudEnvironment);

                var tokenProvider = new AzureAdTokenProvider(tokenCredentials);

                _context = new CloudMediaContext(new Uri(_RESTAPIEndpoint), tokenProvider);
                Guid jobuid = Guid.NewGuid();
                IAsset result = null;

                if (args[0] == "redact")
                {                    
                    if (args[1].StartsWith("nb:cid"))
                    {
                        result = RunFaceRedactionJobFromExistingAsset(jobuid, args[1],
                                    @"config.json");
                    }
                    else
                    {
                        result = RunFaceRedactionJobFromNewAsset(jobuid, args[1],
                                    @"config.json");
                    }                    
                }
                else
                {
                    if (args[0] == "detect")
                    {
                        if (args[1].StartsWith("nb:cid"))
                        {
                            result = RunFaceDetectionJobFromExistingAsset(jobuid, args[1],
                                      @"faceconfig.json");
                        }
                        else
                        {
                            result = RunFaceDetectionJobFromNewAsset(jobuid,args[1],
                                      @"faceconfig.json");
                        }
                    }
                }                              
                // Download the job output asset.
                DownloadAsset(result, "result_" + jobuid);
            }
            else
            {
                Console.WriteLine("Invalid Usage: MediaFaceRedact.exe [redact/detect] [existing uid/new video file name]");
            }
        }

        static void PrintAssets()
        {
            StringBuilder builder = new StringBuilder();

            foreach (var asset1 in _context.Assets)
            {
                /// Display the collection of assets.
                builder.AppendLine("");
                builder.AppendLine("******ASSET******");
                builder.AppendLine("Asset ID: " + asset1.Id);
                builder.AppendLine("Name: " + asset1.Name);
                builder.AppendLine("==============");
                builder.AppendLine("******ASSET FILES******");

                // Display the files associated with each asset. 
                foreach (IAssetFile fileItem in asset1.AssetFiles)
                {
                    builder.AppendLine("Name: " + fileItem.Name);
                    builder.AppendLine("Size: " + fileItem.ContentFileSize);
                    builder.AppendLine("==============");
                }
            }
            Console.Write(builder.ToString());
        }

        static IAsset GetAsset(string assetId)
        {
            // Use a LINQ Select query to get an asset.
            var assetInstance =
                from a in _context.Assets
                where a.Id == assetId
                select a;
            // Reference the asset as an IAsset.
            IAsset asset = assetInstance.FirstOrDefault();

            return asset;
        }

        static IAsset RunFaceDetectionJobFromNewAsset(Guid jobuid, string inputMediaFilePath, string configurationFile)
        {

            // Create an asset and upload the input media file to storage.
            IAsset asset = CreateAssetAndUploadSingleFile(inputMediaFilePath,
                "My Face Detection Input Asset " + jobuid,
                AssetCreationOptions.None);
            return RunFaceDetectionJob(jobuid, asset, configurationFile);
        }

        static IAsset RunFaceDetectionJobFromExistingAsset(Guid jobuid, string assetuid, string configurationFile)
        {            
            IAsset asset = GetAsset(assetuid);
            return RunFaceDetectionJob(jobuid, asset, configurationFile);
        }


        static IAsset RunFaceDetectionJob(Guid jobuid, IAsset iAsset, string configurationFile)
        {            
            // Declare a new job.
            IJob job = _context.Jobs.Create("My Face Detection Job New "  + jobuid);

            // Get a reference to Azure Media Redactor.
            string MediaProcessorName = "Azure Media Face Detector";

            var processor = GetLatestMediaProcessorByName(MediaProcessorName);

            // Read configuration from the specified file.
            string configuration = File.ReadAllText(configurationFile);

            // Create a task with the encoding details, using a string preset.
            ITask task = job.Tasks.AddNew("My Face Detection Task New " + jobuid,
                processor,
                configuration,
                TaskOptions.None);

            // Specify the input asset.
            task.InputAssets.Add(iAsset);

            // Add an output asset to contain the results of the job.
            task.OutputAssets.AddNew("My Face Detection Output Asset New " + jobuid, AssetCreationOptions.None);

            // Use the following event handler to check job progress.  
            job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);

            // Launch the job.
            job.Submit();

            // Check job execution and wait for job to finish.
            Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);

            progressJobTask.Wait();

            // If job state is Error, the event handling
            // method for job progress should log errors.  Here we check
            // for error state and exit if needed.
            if (job.State == JobState.Error)
            {
                ErrorDetail error = job.Tasks.First().ErrorDetails.First();
                Console.WriteLine(string.Format("Error: {0}. {1}",
                                error.Code,
                                error.Message));
                return null;
            }

            return job.OutputMediaAssets[0];
        }

        static IAsset RunFaceRedactionJobFromNewAsset(Guid jobuid, string inputMediaFilePath, string configurationFile)
        {
            // Create an asset and upload the input media file to storage.
            IAsset asset = CreateAssetAndUploadSingleFile(inputMediaFilePath,
                "My Face Redaction Input Asset " + inputMediaFilePath,
                AssetCreationOptions.None);
            return RunFaceRedactionJob(jobuid, asset, configurationFile);
        }

        static IAsset RunFaceRedactionJobFromExistingAsset(Guid jobuid, string assetuid, string configurationFile)
        {            
            IAsset asset = GetAsset(assetuid);
            return RunFaceRedactionJob(jobuid, asset, configurationFile);
        }

        static IAsset RunFaceRedactionJob(Guid jobuid, IAsset asset, string configurationFile)
        {           
            // Declare a new job.
            IJob job = _context.Jobs.Create("My Face Redaction Job New " + jobuid);

            // Get a reference to Azure Media Redactor.
            string MediaProcessorName = "Azure Media Redactor";

            var processor = GetLatestMediaProcessorByName(MediaProcessorName);

            // Read configuration from the specified file.
            string configuration = File.ReadAllText(configurationFile);

            // Create a task with the encoding details, using a string preset.
            ITask task = job.Tasks.AddNew("My Face Redaction Task New " + jobuid,
                processor,
                configuration,
                TaskOptions.None);

            // Specify the input asset.
            task.InputAssets.Add(asset);

            // Add an output asset to contain the results of the job.
            task.OutputAssets.AddNew("My Face Redaction Output Asset " + jobuid , AssetCreationOptions.None);

            // Use the following event handler to check job progress.  
            job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);

            // Launch the job.
            job.Submit();

            // Check job execution and wait for job to finish.
            Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);

            progressJobTask.Wait();

            // If job state is Error, the event handling
            // method for job progress should log errors.  Here we check
            // for error state and exit if needed.
            if (job.State == JobState.Error)
            {
                ErrorDetail error = job.Tasks.First().ErrorDetails.First();
                Console.WriteLine(string.Format("Error: {0}. {1}",
                                error.Code,
                                error.Message));
                return null;
            }

            return job.OutputMediaAssets[0];
        }

        static IAsset CreateAssetAndUploadSingleFile(string filePath, string assetName, AssetCreationOptions options)
        {
            IAsset asset = _context.Assets.Create(assetName, options);

            var assetFile = asset.AssetFiles.Create(Path.GetFileName(filePath));
            assetFile. Upload(filePath);

            return asset;
        }

        static void DownloadAsset(IAsset asset, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (IAssetFile file in asset.AssetFiles)
            {
                file.Download(Path.Combine(outputDirectory, file.Name));
            }
        }

        static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            var processor = _context.MediaProcessors
            .Where(p => p.Name == mediaProcessorName)
            .ToList()
            .OrderBy(p => new Version(p.Version))
            .LastOrDefault();

            if (processor == null)
                throw new ArgumentException(string.Format("Unknown media processor",
                                       mediaProcessorName));

            return processor;
        }

        static private void StateChanged(object sender, JobStateChangedEventArgs e)
        {
            Console.WriteLine("Job state changed event:");
            Console.WriteLine("  Previous state: " + e.PreviousState);
            Console.WriteLine("  Current state: " + e.CurrentState);

            switch (e.CurrentState)
            {
                case JobState.Finished:
                    Console.WriteLine();
                    Console.WriteLine("Job is finished.");
                    Console.WriteLine();
                    break;
                case JobState.Canceling:
                case JobState.Queued:
                case JobState.Scheduled:
                case JobState.Processing:
                    Console.WriteLine("Please wait...\n");
                    break;
                case JobState.Canceled:
                case JobState.Error:
                    // Cast sender as a job.
                    IJob job = (IJob)sender;
                    // Display or log error details as needed.
                    // LogJobStop(job.Id);
                    break;
                default:
                    break;
            }
        }
    }
}