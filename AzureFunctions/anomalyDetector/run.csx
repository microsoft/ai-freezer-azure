#r "Microsoft.WindowsAzure.Storage"
#r "Newtonsoft.Json"
#r "System.Text.Json"

using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public static readonly string emailAlertUrl = Environment.GetEnvironmentVariable("EMAIL_ALERT_URL");
public static readonly HttpClient client1 = new HttpClient();

// Anomaly detection API secrets
public static readonly string subscriptionKey = Environment.GetEnvironmentVariable("ANOMALY_DETECTOR_KEY");
public static readonly string endpoint = Environment.GetEnvironmentVariable("ANOMALY_DETECTOR_ENDPOINT");

const string latestPointDetectionUrl = "/anomalydetector/v1.0/timeseries/last/detect";
public const string batchDetectionUrl = "/anomalydetector/v1.0/timeseries/entire/detect";

public static DateTimeOffset targetTime;

public static async Task Run(TimerInfo myTimer, CloudTable inputTable, ILogger log)
{
    log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
    
    // Get traget time from when to start reading the data
    targetTime = DateTime.UtcNow;
    targetTime = targetTime.AddHours(-6);
    log.LogInformation($"Target start time is: {targetTime}");

    TableQuery<DataPoint> rangeQuery = new TableQuery<DataPoint>().Where(
        TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.GreaterThan, targetTime));

    // Execute the query and loop through the results
    List<DataPoint> data = new List<DataPoint>();
    foreach (DataPoint entity in 
    await inputTable.ExecuteQuerySegmentedAsync(rangeQuery, null))
    {
        data.Add(new DataPoint() {Timestamp = entity.Timestamp, temperature = entity.temperature});
    }

    // Sort data by Timestamp
    data.Sort((dp1, dp2) => DateTimeOffset.Compare(dp1.Timestamp, dp2.Timestamp));
    
    List<FormatedData> formatedData = new List<FormatedData>();
    data.ForEach( delegate(DataPoint point)
    {
        formatedData.Add(new FormatedData() { timestamp = point.Timestamp.ToString("yyyy-MM-ddTHH:mm:00Z"), value = point.temperature});
    });

    var options = new JsonSerializerOptions
    {
        IgnoreNullValues = true,
        // PropertyNamingPolicy = new LowerCaseNamingPolicy()
    };

    List<JsonFormat> jsonFormat = new List<JsonFormat>();
    jsonFormat.Add(new JsonFormat() {series = formatedData, granularity = "minutely", customInterval = 1, period = 90, sensitivity = 85});
    string dataToSend = JsonSerializer.Serialize(jsonFormat, options);

    // Call anomaly detection API
    var anomalies = detectAnomaliesBatch(dataToSend, log);

    if (anomalies != null){
        var json = JsonSerializer.Serialize(anomalies);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client1.PostAsync(emailAlertUrl, content);
        log.LogInformation(response.ToString());
    }
}



static async Task<string> Request(string apiAddress, string endpoint, string subscriptionKey, string requestData)
{
    using (HttpClient client = new HttpClient { BaseAddress = new Uri(apiAddress) })
    {
        System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);

        var content = new StringContent(requestData, Encoding.UTF8, "application/json");
        var res = await client.PostAsync(endpoint, content);
        return await res.Content.ReadAsStringAsync();
    }
}

static string detectAnomaliesBatch(string requestData, ILogger log)
{
    log.LogInformation("Detecting anomalies as a batch");
   
    requestData = requestData.TrimEnd(']').TrimStart('[');

    //construct the request
    var result = Request(
        endpoint,
        batchDetectionUrl,
        subscriptionKey,
        requestData).Result;

    //deserialize the JSON object, and display it
    dynamic jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(result);
    System.Console.WriteLine(jsonObj);

    string foundAnomalies = "Anomalies detected in the following data positions: ";

    if (jsonObj["code"] != null)
    {
        System.Console.WriteLine($"Detection failed. ErrorCode:{jsonObj["code"]}, ErrorMessage:{jsonObj["message"]}");
        
        log.LogInformation($"Detection failed. ErrorCode:{jsonObj["code"]}, ErrorMessage:{jsonObj["message"]}");
    }
    else
    {
        // log.LogInformation(result);
        //Find and display the positions of anomalies in the data set
        bool[] anomalies = jsonObj["isAnomaly"].ToObject<bool[]>();
        System.Console.WriteLine("\nAnomalies detected in the following data positions:");
        log.LogInformation("\nAnomalies detected in the following data positions:");
        for (var i = 0; i < anomalies.Length; i++)
        {
            if (anomalies[i])
            {
                System.Console.Write(i + ", ");
                log.LogInformation(i + ", ");
                foundAnomalies += i;
                foundAnomalies += ", ";
            }
        }
        if (anomalies.Any(item => item == true))
        {
            return foundAnomalies;
        }
    }
    return null;
}

public class FormatedData
{
    public string timestamp { get; set; }
    public string value { get; set; }
}

public class DataPoint : TableEntity
{
    [JsonPropertyName("value")]
    public string temperature { get; set;}
    public string timestamp { get; set; }
    
}

public class JsonFormat
{
    public List<FormatedData> series { get; set; }
    public string granularity { get; set; }
    public int customInterval { get; set; }
    public int period { get; set; }
    // public float maxAnomalyRatio { get; set; }
    public int sensitivity { get; set; }
}

public class LowerCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) =>
        name.ToLower();
}
