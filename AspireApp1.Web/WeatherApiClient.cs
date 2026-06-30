namespace AspireApp1.Web;

public class WeatherApiClient(HttpClient httpClient)
{
    /// <summary>
    /// Calling http://apiService/weatherforecast
    /// </summary>
    public async Task<WeatherForecast[]> GetWeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default)
    {
        List<WeatherForecast>? forecasts = null;

        try
        {
            var response = httpClient.GetFromJsonAsAsyncEnumerable<WeatherForecast>("/weatherforecast", cancellationToken);
            if (response is not null)
            {
                await foreach (var forecast in response)
                {
                    if (forecasts?.Count >= maxItems)
                    {
                        break;
                    }
                    if (forecast is not null)
                    {
                        forecasts ??= new List<WeatherForecast>();
                        forecasts.Add(forecast);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling ApiErrorService: {ex.Message}");
        }

        return forecasts?.ToArray() ?? [];
    }

    /// <summary>
    /// Calling http://apiService/errorcall
    /// </summary>
    public async Task<WeatherForecast[]> GetErrorWeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default)
    {
        List<WeatherForecast>? forecasts = null;

        try
        {
            var response = httpClient.GetFromJsonAsAsyncEnumerable<WeatherForecast>("/errorcall", cancellationToken);
            if (response is not null)
            {
                await foreach (var forecast in response)
                {
                    if (forecasts?.Count >= maxItems)
                    {
                        break;
                    }
                    if (forecast is not null)
                    {
                        forecasts ??= new List<WeatherForecast>();
                        forecasts.Add(forecast);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling ApiErrorService: {ex.Message}");
        }

        return forecasts?.ToArray() ?? [];
    }

    /// <summary>
    /// Calling http://apiService/errorcall2
    /// </summary>
    public async Task<WeatherForecast[]> GetError2WeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default)
    {
        List<WeatherForecast>? forecasts = null;

        try
        {
            var response = httpClient.GetFromJsonAsAsyncEnumerable<WeatherForecast>("/errorcall2", cancellationToken);
            if (response is not null)
            {
                await foreach (var forecast in response)
                {
                    if (forecasts?.Count >= maxItems)
                    {
                        break;
                    }
                    if (forecast is not null)
                    {
                        forecasts ??= new List<WeatherForecast>();
                        forecasts.Add(forecast);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling ApiErrorService: {ex.Message}");
        }

        return forecasts?.ToArray() ?? [];
    }
}

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
