
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Orchestration;
using Baseball_HallofFame_OpenAI_TextGenerator;
using Newtonsoft.Json;
using Baseball_HallofFame_OpenAI_TextGenerator.Shared;
using Microsoft.Bing.WebSearch;


namespace Baseball_HallofFame_OpenAI_TextGenerator.MySkillsDirectory
{
    public class MyBingSkill
    {        
        private List<WebSearchResult> webSearchResults = new List<WebSearchResult>();
        private MLBBatterInfo mlbBatterInfo = new MLBBatterInfo();
        public static string footNotes = string.Empty;
        private string webSearchResultsString = "Web search results:\r\n\r\n";
             

        [SKFunction("Return Bing Results")]
        [SKFunctionContextParameter(Name = "MLBBatterInfo", Description = "Player Input")]
        public async Task<string> OriginalBing(SKContext context)
        {
            var player = context["MLBBatterInfo"];
            MLBBatterInfo mlbBatterInfo = mlbBatterInfo = JsonConvert.DeserializeObject<MLBBatterInfo>(player) ?? new MLBBatterInfo();

            var redisCache = new Cache.Redis();

            var searchString = string.Format("{0} baseball Hall of Fame", mlbBatterInfo?.FullPlayerName);
            bool isBingSearchWorking = true;
            var bingSearchId = 0;

            try
            {
                webSearchResults = redisCache.GetWebSearchResults(mLBBatterInfo: mlbBatterInfo, searchString);
            }
            catch (Exception)
            {

                webSearchResults = new List<WebSearchResult>();
            }

            footNotes = string.Empty;
            if (webSearchResults.Count > 0)
            {               

                // Itertate over the Bing Web Pages (Cache)
                foreach (var bingWebPage in webSearchResults)
                {
                    bingSearchId++;

                    webSearchResultsString += string.Format("[{0}]: \"{1}: {2}\"\r\nURL: {3}\r\n\r\n",
                        bingSearchId, bingWebPage.Name, bingWebPage.Snippet, bingWebPage.Url);

                    footNotes += string.Format("[{0}]: {1}: {2}  \r\n",
                        bingSearchId, bingWebPage.Name, bingWebPage.Url);
                }
                
                return Newtonsoft.Json.JsonConvert.SerializeObject(webSearchResults);
            }
            else
            {
             
                var bingSearchKey = System.Environment.GetEnvironmentVariable("BING_SEARCH_KEY");
                var bingSearchClient = new WebSearchClient(new ApiKeyServiceClientCredentials(bingSearchKey));
                var bingWebData = await bingSearchClient.Web.SearchAsync(query: searchString, count: 8);


                if (bingWebData?.WebPages?.Value?.Count > 0)
                {
                    isBingSearchWorking = true;
                    // Itertate over the Bing Web Pages (Non-Cache Results)
                    foreach (var bingWebPage in bingWebData.WebPages.Value)
                    {
                        bingSearchId++;

                        webSearchResultsString += string.Format("[{0}]: \"{1}: {2}\"\r\nURL: {3}\r\n\r\n",
                            bingSearchId, bingWebPage.Name, bingWebPage.Snippet, bingWebPage.Url);

                        footNotes += string.Format("[{0}]: {1}: {2}  \r\n",
                            bingSearchId, bingWebPage.Name, bingWebPage.Url);

                        webSearchResults.Add(new WebSearchResult
                        {
                            Id = bingSearchId,
                            Name = bingWebPage.Name,
                            Snippet = bingWebPage.Snippet,
                            Url = bingWebPage.Url
                        });
                    }

                    // Add to Cache - WebSearchResults
                    redisCache.AddWebSearchResults(mlbBatterInfo, searchString, webSearchResults);
                }
                return Newtonsoft.Json.JsonConvert.SerializeObject(webSearchResults );
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(new WebSearchResult());
        }
    }
}
