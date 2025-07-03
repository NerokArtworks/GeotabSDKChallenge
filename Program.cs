using Geotab.Checkmate;
using Geotab.Checkmate.ObjectModel;
using Geotab.Checkmate.ObjectModel.Engine;
using Geotab.Checkmate.Web;

class Program
{
    private static readonly Dictionary<Id, DateTime> LastReadTimestamps = new();

    static async Task Main(string[] args)
    {
        if (args.Length != 4)
        {
            Log("Usage: dotnet run <server> <database> <username> <password>");
            return;
        }

        string server = args[0];
        string database = args[1];
        string username = args[2];
        string password = args[3];

        // Use CancellationTokenSource to support graceful cancellation.
        using var cts = new CancellationTokenSource();

        // Handle Ctrl+C / SIGINT to trigger cancellation.
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            Log("Cancellation requested...");
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var cancellationToken = cts.Token;

        // Authenticate and get API client.
        Log("Starting authentication with Geotab API...");
        var api = await UseApiWithCredentials(server, database, username, password, cancellationToken);

        if (api.LoginResult == null)
        {
            Log("Could not authenticate. End of program.", "ERROR");
            return;
        }

        Log("Authentication successful.");

        // Run backup cycles until cancellation.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunBackupCycle(api, cancellationToken);

                // Wait 60 seconds to respect API query rate limits.
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            }
            catch (InvalidApiOperationException ex)
            {
                Log(ex.Message, "ERROR");
                return;
            }
            catch (WebServerInvokerJsonException ex)
            {
                Log(ex.Message, "ERROR");
                await Task.Delay(10000, cancellationToken);
            }
            catch (OverLimitException ex)
            {
                Log($"User has exceeded the query limit, retrying after a minute... {ex.Message}", "ERROR");
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                Console.WriteLine("Restarting backup...");
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log(ex.Message, "ERROR");
                Log("Press any key to exit...");
                return;
            }
        }
    }

    static async Task<API> UseApiWithCredentials(string server, string database, string username, string password, CancellationToken token)
    {
        var api = new API(username, password, null, database, server);
        try
        {
            await api.AuthenticateAsync(token);
        }
        catch (OperationCanceledException)
        {
            Log("Authentication canceled by the user.");
        }
        catch (Exception ex)
        {
            Log($"Failed to authenticate user: {ex.Message}", "ERROR");
        }
        return api;
    }

    static async Task RunBackupCycle(API api, CancellationToken cancellationToken)
    {
        Log("Starting sync backup process...");

        Log("Fetching devices...");
        var devices = await api.CallAsync<IList<Device>>("Get", typeof(Device), null, cancellationToken);

        if (devices == null || devices.Count == 0)
        {
            Log("No devices found.");
            return;
        }

        Log($"Retrieved {devices.Count} devices.");

        // Prepare batch calls for coordinates and odometer readings.
        var calls = devices
            .SelectMany(device => new List<object[]> { GetCoordinates(device), GetOdometer(device) })
            .ToList();

        // Batch API calls in groups of 100 to respect MultiCall’s sub-request limit.
        // 50 devices × 2 calls each = 100 calls, hitting the limit exactly.
        // To ensure scalability, I decided to split the calls into batches of up to 100 using .Chunk(100).
        var multiCallsResults = new List<object?>();
        foreach (var callBatch in calls.Chunk(100))
        {
            var resultBatch = await api.MultiCallAsync(callBatch);
            multiCallsResults.AddRange(resultBatch);
        }

        var deviceStatusInfoList = multiCallsResults.OfType<IList<DeviceStatusInfo>>().Select(d => d?.FirstOrDefault());
        var statusDataList = multiCallsResults.OfType<IList<StatusData>>().Select(s => s?.FirstOrDefault());

        // Filter devices with new activity since last read timestamp.
        var vehicleBackupList = devices
            .Where(device =>
            {
                // Find the latest status info for the device.
                var statusInfo = deviceStatusInfoList.FirstOrDefault(d => d?.Device?.Id == device.Id);
                if (statusInfo?.DateTime == null || device.Id == null) return false;

                var newTimestampUtc = statusInfo.DateTime.Value.ToUniversalTime();

                if (!LastReadTimestamps.TryGetValue(device.Id, out DateTime lastTimestamp) || newTimestampUtc > lastTimestamp)
                {
                    // Update stored last read timestamp for the device.
                    LastReadTimestamps[device.Id] = newTimestampUtc;
                    Log($" New activity detected for device id {device.Id}, marking for backup.", " ");
                    return true;
                }

                // No new activity since last read.
                return false;
            })
            // Map filtered devices into a data structure with odometer info.
            .Select(device =>
            {
                var goDevice = device as GoDevice;
                var statusInfo = deviceStatusInfoList.FirstOrDefault(d => d?.Device?.Id == device.Id);
                var statusData = statusDataList.FirstOrDefault(s => s?.Device?.Id == device.Id);

                return new VehicleWithOdometer
                {
                    Id = device.Id,
                    Timestamp = statusInfo?.DateTime,
                    Vin = goDevice?.VehicleIdentificationNumber,
                    Latitude = statusInfo?.Latitude,
                    Longitude = statusInfo?.Longitude,
                    Odometer = statusData?.Data
                };
            })
            .ToList();

        Log($"Writing CSV files for {vehicleBackupList.Count} devices...");
        await WriteParallelCsv(vehicleBackupList, cancellationToken);

        Log("Sync backup completed successfully. Waiting for the next cycle...\n");
    }

    static async Task WriteParallelCsv(IList<VehicleWithOdometer> vehicles, CancellationToken cancellationToken)
    {
        string folderPath = "VehicleBackups";

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        await Parallel.ForEachAsync(vehicles, cancellationToken, async (v, ct) =>
        {
            string filePath = Path.Combine(folderPath, $"{v.Id}.csv");
            bool fileExists = File.Exists(filePath);

            await using var writer = new StreamWriter(filePath, append: true);

            if (!fileExists)
            {
                await writer.WriteLineAsync(v.CsvHead.AsMemory(), ct);
            }

            await writer.WriteLineAsync(v.ToCsv().AsMemory(), ct);
        });
    }

    static object[] GetCoordinates(Device device)
    {
        return
        [
            "Get", typeof(DeviceStatusInfo), new
            {
                search = new DeviceStatusInfoSearch
                {
                    DeviceSearch = new DeviceSearch(device.Id)
                },
                propertySelector = new PropertySelector
                {
                    Fields =
                    [
                        nameof(DeviceStatusInfo.Latitude),
                        nameof(DeviceStatusInfo.Longitude),
                        nameof(DeviceStatusInfo.DateTime),
                        nameof(DeviceStatusInfo.Device),
                    ],
                    IsIncluded = true
                }
            },
            typeof(IList<DeviceStatusInfo>)
        ];
    }

    static object[] GetOdometer(Device device)
    {
        return
        [
            "Get", typeof(StatusData), new
            {
                search = new StatusDataSearch
                {
                    DeviceSearch = new DeviceSearch(device.Id),
                    DiagnosticSearch = new DiagnosticSearch(KnownId.DiagnosticOdometerAdjustmentId),
                    FromDate = DateTime.MaxValue
                },
                propertySelector = new PropertySelector
                {
                    Fields =
                    [
                        nameof(StatusData.Data),
                        nameof(StatusData.DateTime)
                    ],
                    IsIncluded = true
                }
            },
            typeof(IList<StatusData>)
        ];
    }

    static void Log(string message, string level = "INFO")
    {
        Console.WriteLine($"[{level}] {message}");
    }
}