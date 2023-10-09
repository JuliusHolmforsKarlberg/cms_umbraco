using cms_1.Business.ScheduleJobs;
using cms_1.Business.Models;

namespace cms_1.Business.Registers
{
	public static class UmbracoBuilderHostedServiceExtensions
	{
        public static IUmbracoBuilder AddCustomHostedServices(this IUmbracoBuilder builder)
        {
            builder.Services.AddHostedService<TTImport>();
            return builder;
        }

    }
}
