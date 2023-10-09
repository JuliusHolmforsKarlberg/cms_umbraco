using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.HostedServices;
using Newtonsoft.Json;
using cms_1.Business.Models;
using System.Net;

namespace cms_1.Business.ScheduleJobs
{
    public class TTImport : RecurringHostedServiceBase
    {
        private readonly IRuntimeState _runtimeState;
        private readonly IContentService _contentService;
        private readonly ILogger<TTImport> _logger;
        private readonly ICoreScopeProvider _scopeProvider;
        private readonly IUmbracoContextFactory _umbracoContextFactory;
        private static TimeSpan HowOftenWeRepeat => TimeSpan.FromMinutes(2);
        private static TimeSpan DelayBeforeWeStart => TimeSpan.FromMinutes(1);

        public TTImport(
            IRuntimeState runtimeState,
            IContentService contentService,
            ILogger<TTImport> logger,
            IUmbracoContextFactory umbracoContextFactory,
            ICoreScopeProvider scopeProvider)
            : base(logger, HowOftenWeRepeat, DelayBeforeWeStart)
        {
            _runtimeState = runtimeState;
            _contentService = contentService;
            _logger = logger;
            _umbracoContextFactory = umbracoContextFactory;
            _scopeProvider = scopeProvider;
        }

        public override Task PerformExecuteAsync(object? state)
        {
            if (_runtimeState.Level is not RuntimeLevel.Run)
            {
                return Task.CompletedTask;
            }

            using ICoreScope scope = _scopeProvider.CreateCoreScope();
            _logger.LogInformation("Start TT Nyheter Import job");

            var homePage = _contentService.GetRootContent().First();
            IPublishedContent BloggLista = null;
            IEnumerable<IPublishedContent>? children = null;

            using (var umbracoContextReference = _umbracoContextFactory.EnsureUmbracoContext())
            {
                var content = umbracoContextReference?.UmbracoContext?.Content?.GetById(homePage.Id);

                if (content != null)
                {
                    BloggLista = content.ChildrenOfType("pressReleases").FirstOrDefault();
                    _logger.LogInformation("Running tt import job " + BloggLista.Name + " " + BloggLista.Id);
                }

                if (BloggLista != null)
                {
                    var parentId = Guid.Parse(BloggLista.Key.ToString());
                    children = BloggLista.Children;

                    var ttData = HamtaData().Take(5); // Limit to 5 items

                    if (ttData != null)
                    {
                        foreach (var item in ttData)
                        {
                            bool hasMatch = children != null ? children.Any(x => x.Name?.ToLower() == item.Title.ToLower()) : true;
                            if (!hasMatch)
                            {
                                var pressItem = _contentService.Create(item.Title, parentId, "pressItem");

                                pressItem.SetValue("pageid", item.Id);
                                pressItem.SetValue("title", item.Title);
                                pressItem.SetValue("leadtext", item.Leadtext);

                                _contentService.SaveAndPublish(pressItem);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogError("TT Nyheter data is null or empty.");
                    }
                }

                _logger.LogInformation("End TT Nyheter Import job");
            }

            scope.Complete();
            return Task.CompletedTask;
        }

        private IList<TTModel> HamtaData()
        {
            var feedUrl = new Uri("https://via.tt.se/json/v2/releases?publisher=686463&channels=696961");
            string jsonData;

            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            using (var httpClient = new HttpClient())
            {
                var response = httpClient.GetAsync(feedUrl).ConfigureAwait(false).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("TT nyheter Respons fel");
                    return null;
                }

                var ttData = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                try
                {
                    var ttRootObject = JsonConvert.DeserializeObject<RootModel>(ttData);
                    return ttRootObject?.Releases;
                }
                catch (JsonSerializationException jse)
                {
                    _logger.LogInformation("TT nyheter deserialize error: " + jse.Message);
                    return null;
                }
                catch (Exception e)
                {
                    _logger.LogError("Error while fetching TT Nyheter data: " + e.Message);
                    return null;
                }
            }
        }
    }
}
