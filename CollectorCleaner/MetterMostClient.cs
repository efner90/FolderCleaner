using RestSharp;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CollectorCleaner
{
    class MetterMostClient
    {
        private readonly string _webhookUrl;
        private readonly ILogger _logger;

        public MetterMostClient(string webhookUrl)
        {
            _webhookUrl = webhookUrl;
            _logger = Log.Logger.ForContext("ClassType", GetType());

        }

        public void SendMessageToMM(string message)
        {
            using (var client = new RestClient())
            {
                RestRequest request = new RestRequest(_webhookUrl, Method.Post);
                var payload = new { text = message };
                request.AddJsonBody(payload);

                try
                {
                    var response = client.Execute(request);
                    if (response.IsSuccessful)
                    {
                        _logger.Information("Message sent");
                    }
                    else
                    {
                        _logger.Information("Message not sent");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "error with message in MMclient");
                }
            }
        }
    }
}
