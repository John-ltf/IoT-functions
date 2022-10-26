using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace IoTTelemetryData
{
    public class TelemetryDataBuilder
    {
        protected TelemetryData telemetryData;
        public TelemetryDataBuilder() => telemetryData = new TelemetryData();
        public TelemetryData Build() => telemetryData;
        public static TelemetryDataBuilder newTelemetryData => new TelemetryDataBuilder();

        public static TelemetryData newTelemetryDataRaw(string deviceId, string data) =>
            TelemetryDataBuilder
            .newTelemetryData
            .genGuid()
            .withDeviceId(deviceId)
            .withData(data)
            .setDatetime()
            .setTTL()
            .Build();
        public TelemetryDataBuilder genGuid()
        {
            telemetryData.id = Guid.NewGuid();
            return this;
        }
        public TelemetryDataBuilder withDeviceId(string deviceId)
        {
            telemetryData.deviceId = deviceId;
            return this;
        }
        public TelemetryDataBuilder withData(string data)
        {
            telemetryData.telemetry= JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(data);
            return this;
        }
        public TelemetryDataBuilder setDatetime()
        {
            if(telemetryData.telemetry is not null && telemetryData.telemetry.ContainsKey("time"))
            {
                DateTime dataTime = DateTime.ParseExact(telemetryData.telemetry["time"].ToString(), "yyyy-MM-dd:HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                telemetryData.telemetry["time"] = dataTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
            }
            return this;
        }
        public TelemetryDataBuilder setTTL()
        {
            if(telemetryData.telemetry is not null && telemetryData.telemetry.ContainsKey("ttl"))
            {
                try
                {
                    telemetryData.ttl = Int32.Parse(telemetryData.telemetry["ttl"]);
                    telemetryData.telemetry.Remove("ttl");
                    if(telemetryData.ttl > 0)
                        telemetryData.ttl = telemetryData.ttl * 60 * 60 * 24; //from days to seconds
                    else
                        telemetryData.ttl = -1;

                }
                catch (FormatException)
                {
                    //log.LogError($"Unable to parse ttl '{item["ttl"]}'");
                }
            }
            return this;
        }
    }
    public class TelemetryData
    {
        public Dictionary<string, dynamic> telemetry { get; set;}
        public string deviceId { get; set;}
        public int ttl { get; set;}
        public Guid id { get; set;}

    }
}