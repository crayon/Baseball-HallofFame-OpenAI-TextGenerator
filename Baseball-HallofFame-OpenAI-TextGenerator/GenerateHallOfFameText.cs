using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Baseball_HallofFame_OpenAI_TextGenerator.Shared;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using Microsoft.SemanticKernel.Orchestration;
using Baseball_HallofFame_OpenAI_TextGenerator.MySkillsDirectory;


namespace Baseball_HallofFame_OpenAI_TextGenerator
{
    public class GenerateHallOfFameText
    {
        private readonly ILogger _logger;
        private List<WebSearchResult> webSearchResults = new List<WebSearchResult>();
        private MLBBatterInfo mlbBatterInfo = new MLBBatterInfo();
        private string webSearchResultsString = "Web search results:\r\n\r\n";
        public GenerateHallOfFameText(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GenerateHallOfFameText>();
        }

        [Function("GenerateHallOfFameText")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {            
            #region Housekeeping
            _logger.LogInformation("GenerateHallOfFameText - Enter");
            HttpResponseData response = req.CreateResponse();
            var requestBodyString = string.Empty;
            
            // JDM - Get is only here to making development and debugging easier, Only post for prod
            if (req.Method.ToUpper() == "POST")
            {
                requestBodyString = await ReadBodyAsStringAsync(req.Body);
            }
            else
            {
                requestBodyString = "{\r\n    \"FullPlayerName\" : \"Larry Bowa\",\r\n    \"YearsPlayed\": 23,\r\n    \"HR\" : 660,\r\n    \"TotalPlayerAwards\" : 25,\r\n    \"HallOfFameProbability\" : 99.9    \r\n}";
            }
            #endregion
            
            #region Azure Keys Etc....
            var modelAlias = "HOF-Semantic-Kernal-Modal";  // LLM AI Model Alias 'text-davinci-003-demo' Does not work
            var deploymentID = System.Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT"); 
            var endpoint = System.Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"); 
            var openAI = System.Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            #endregion

            #region Build SK
            // Create and Configure the Kernel
            var myKernel = Kernel.Builder.Build();
            myKernel.Config.AddAzureChatCompletionService(modelAlias, deploymentID, endpoint, openAI);
            #endregion

            #region Instantiate Redis
            //Instantiate the Cache
            var redisCache = new Cache.Redis();
            #endregion           

            _logger.LogInformation("GenerateHallOfFameText - Request Body: " + requestBodyString);

            // No Data, kick it back
            if (requestBodyString.Length < 20)
            {
                #region Error, body is empty
                _logger.LogInformation("GenerateHallOfFameText - Not Enough to process error");
                response.StatusCode = HttpStatusCode.BadRequest;
                response.WriteString("Message request does not have enough inofrmation in the body request. Need MLBBatterInfo contract.");
                return response;
                #endregion
            }
            // Cache - Initialize Cache (Redis) & Check Cache

            var narratives = new List<NarrativeResult>();
            // Cache - Determine if should pull from cache
            if (Util.UseRedisCache(redisCache.IsRedisConnected))
            {
                _logger.LogInformation("GenerateHallOfFameText - Random selection attempting Cache");
                narratives = redisCache.GetNarratives(mlbBatterInfo);
            }          

            // Cache - Return the Narrative from Cache
            if (narratives.Count > 0)
            {
                var random = new Random(DateTime.Now.Second).Next(0, narratives.Count - 1);
                var selectedNarrative = narratives[random].Text;
                _logger.LogInformation("GenerateHallOfFameText - MLBBatterInfo - Narrative FOUND in Cache");

                // Successful response (OK)
                response.StatusCode = HttpStatusCode.OK;
                response.WriteString(selectedNarrative);
            }
            else
            {
                // OpenAI - Text Generator Components

                mlbBatterInfo = JsonConvert.DeserializeObject<MLBBatterInfo>(requestBodyString) ?? new MLBBatterInfo();
                _logger.LogInformation("GenerateHallOfFameText - MLBBatterInfo Deserialized");

                #region Search Bing - SK Native Function
                //myKernel.ImportSkill(new WebSearchEngineSkill(bingConnector), "bing");
                var mySkill = myKernel.ImportSkill(new MyBingSkill(), "MyBingSkill");
                var myContext = new ContextVariables();
                myContext.Set("MLBBatterInfo", requestBodyString);
                var webSearchResultsString = await myKernel.RunAsync(myContext, mySkill["OriginalBing"]);
                #endregion

                string? responsed = await GenerateOutput(myKernel, JsonConvert.SerializeObject(webSearchResultsString));
                redisCache.AddNarrative(mlbBatterInfo, responsed ?? string.Empty);
                response.StatusCode = HttpStatusCode.OK;
                response.WriteString(responsed + "\n\n\n"+ MyBingSkill.footNotes);

            }   // ENDOF - ELSE PROCESS either BING and/or OPENAI


            _logger.LogInformation("GenerateHallOfFameText - End");
             return response;
        
        }

        // Could refactor into a skill
        private async Task<string?> GenerateOutput(IKernel myKernel, string webSearchResults)
        {
            // This could be in a file...
            var promptInstructions = "The current date is {{$TODAY}}. Using most of the provided Web search results and probability and statistics found in the given query, write a comprehensive reply to the given query. " +
                                "Make sure to cite results using [number] notation of each URL after the reference. " +
                                "If the provided search results refer to multiple subjects with the same name, write separate answers for each subject. " +
                                "Query: An AI model states the probability of baseball hall of fame induction for {{$NAME}} as {{$P}}. {{$NAME}} has played baseball for {{$YEARS}} years. {{$NAME}} has hit {{$DINGERS}} Home Runs, and won {{$AWARDS}} total awards. Provide a detailed case supporting or against {{$NAME}} to be considered for the Hall of Fame.\r\n";


            var myHofFunction = myKernel.CreateSemanticFunction(
                    promptInstructions+' ' + webSearchResults,
                    maxTokens: 4096
                );

            var myContext = new ContextVariables();
            myContext.Set("NAME", mlbBatterInfo?.FullPlayerName);
            myContext.Set("P", mlbBatterInfo?.HallOfFameProbability.ToString("P", CultureInfo.InvariantCulture));
            myContext.Set("YEARS", mlbBatterInfo?.YearsPlayed.ToString());
            myContext.Set("DINGERS", mlbBatterInfo?.HR.ToString());
            myContext.Set("AWARDS", mlbBatterInfo?.TotalPlayerAwards.ToString());
            myContext.Set("TODAY", DateTime.Now.ToString("M/d/yyyy"));

            var output = await myKernel.RunAsync(myContext, myHofFunction);

            var responsed = output + "";
            return responsed;
        }
        static async Task<string> ReadBodyAsStringAsync(Stream body)
        {
            using (StreamReader reader = new StreamReader(body, Encoding.UTF8))
            {
                return await reader.ReadToEndAsync();
            }
        }
        private static async Task BingSearchAsync(IKernel myKernel, string prompt)
        {
            Console.WriteLine("======== Bing Search Skill ========");

            // Run        
            var bingResult = await myKernel.Func("bing", "search").InvokeAsync(prompt);// Have to be able to pass in a parameters for number of results

            Console.WriteLine(prompt);
            Console.WriteLine("----");
            Console.WriteLine(bingResult);
            Console.WriteLine("----");
        }


    }
}
