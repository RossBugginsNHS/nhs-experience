using Microsoft.Extensions.DependencyInjection;

namespace dhc;

public static class HealthCheckProviderOptionsDefaults
{
    public static HealthCheckProviderOptions Defaults(IServiceCollection services)
    {
        var options = new HealthCheckProviderOptions(services);
        options.HealthCheckDataBuilders.Add<HealthCheckDataBuilderBuildFilterId>();
        options.Filters.Add<HealthCheckFilterBmi>();
        options.Filters.Add<HealthCheckFilterBp>();
        options.Filters.Add<HealthCheckFilterSmoking>();
        options.Filters.Add<HealthCheckFilterBmiResult>();
        options.GuidanceFilters.Add<HealthCheckGuidanceFilterBp>();
        options.GuidanceFilters.Add<HealthCheckGuidanceFilterWeight>();
        options.GuidanceFilters.Add<HealthCheckGuidanceFilterSmoking>();
        options.HealthCheckDataBuilders.Add<HealthCheckDataBuilderBuildFilterBasicObs>();
        options.HealthCheckDataBuilders.Add<HealthCheckDataBuilderBuildFilterDemographics>();
        options.HealthCheckDataBuilders.Add<HealthCheckDataBuilderBuildFilterBloodPressure>();
        options.HealthCheckDataBuilders.Add<HealthCheckDataBuilderBuildFilterSmoking>();
   
        options.SetBmiProvider<BmiCalculatorProvider>();

        //Cholesterol
        options.HealthCheckDataBuilders.Add<HealthCheckDataBuilderBuildFilterCholesterol>();
        options.Filters.Add<HealthCheckFilterCholesterol>();
        options.GuidanceFilters.Add<HealthCheckGuidanceFilterCholesterol>();

        options.Filters.Add<HealthCheckFilterDemographics>();
        options.Filters.Add<HealthCheckFilterQRisk>();
        options.GuidanceFilters.Add<HealthCheckGuidanceFilterQrisk>();
        
        return options;
    }
}
