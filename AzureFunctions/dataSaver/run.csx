#r "Newtonsoft.Json"

using System;
using Newtonsoft.Json;

public static void Run(string myIoTHubMessage, ICollector<outTable> outputTable, ILogger log)
{
    log.LogInformation($"C# IoT Hub trigger function processed a message: {myIoTHubMessage}");
    dynamic input = JsonConvert.DeserializeObject(myIoTHubMessage);
    Guid guid = Guid.NewGuid();
    log.LogInformation($"Message guid: {guid}");
    outputTable.Add(
            new outTable() { 
                PartitionKey = "test", 
                RowKey = guid.ToString(), 
                deviceId = input.deviceId.ToString(),
                temperature = input.Temperature}
            );
}

public class outTable
{
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public string deviceId { get; set; }
    public float temperature {get; set;}

}