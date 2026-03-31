using Microsoft.Extensions.Caching.Memory; // Bunu eklemeyi unutma
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace FilmOnerisiProje.Services
{
    public class TmdbService
    {
        // YENİ API KEY'İNİ BURAYA YAZ:
        private readonly string _geminiApiKey = "AIzaSyDDW9T55yU9MshJoSsIVCTNPUv4pffsaxU";
        private readonly string _tmdbApiKey = "1cf9f5a9db38571c82f11041cb25852b";

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache; // Önbellek servisi

        // GELİŞTİRME MODU: True ise API kullanmaz, sahte veri döner.
        // Projeyi bitirdiğinde bunu 'false' yap.
        private readonly bool _useMockData = false;

        public TmdbService(HttpClient httpClient, IMemoryCache memoryCache)
        {
            _httpClient = httpClient;
            _memoryCache = memoryCache;
        }

        public async Task<List<string>> GetGeminiRecommendations(List<string> userMovies)
        {
            
            // 1. ÖNCE MOCK DATA KONTROLÜ (Kotayı korumak için)
            if (_useMockData)
            {
                // Tasarımı test etmen için rastgele filmler döner. API harcamaz.
                return new List<string> {
                    "The Dark Knight", "Inception", "Interstellar", "Prestige",
                    "Memento", "Fight Club", "The Matrix", "Pulp Fiction",
                    "Forrest Gump", "Gladiator"
                };
            }

            // 2. ÖNBELLEK KONTROLÜ (Cache)
            // Eğer aynı filmler için daha önce istek atılmışsa, API'ye gitme, hafızadan getir.
            string cacheKey = "Recs_" + string.Join("_", userMovies).GetHashCode();
            if (_memoryCache.TryGetValue(cacheKey, out List<string> cachedMovies))
            {
                return cachedMovies;
            }

            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    string moviesText = string.Join(", ", userMovies);
                    string prompt = $@"
                    Aşağıdaki film listesini analiz et ve bu zevklere uygun 10 adet film öner: {moviesText}.
                    Cevabı SADECE şu formatta JSON dizisi olarak ver: [""Film1"", ""Film2"", ""Film3""]
                    Markdown yok, açıklama yok, sadece JSON.";

                    var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
                    var jsonContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                    var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key={_geminiApiKey}";

                    var response = await _httpClient.PostAsync(url, jsonContent);

                    if ((int)response.StatusCode == 429) // Çok Fazla İstek Hatası
                    {
                        currentRetry++;
                        await Task.Delay(5000 * currentRetry); // Bekleme süresini katlayarak artır (5sn, 10sn...)
                        continue;
                    }

                    if (!response.IsSuccessStatusCode) return new List<string>();

                    var responseString = await response.Content.ReadAsStringAsync();
                    var responseJson = JObject.Parse(responseString);
                    string aiText = responseJson["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                    if (string.IsNullOrEmpty(aiText)) return new List<string>();

                    // JSON Temizleme
                    int firstBracket = aiText.IndexOf('[');
                    int lastBracket = aiText.LastIndexOf(']');
                    if (firstBracket >= 0 && lastBracket > firstBracket)
                    {
                        aiText = aiText.Substring(firstBracket, lastBracket - firstBracket + 1);
                        var resultList = JsonConvert.DeserializeObject<List<string>>(aiText) ?? new List<string>();

                        // BAŞARILI SONUÇ! Hafızaya (Cache) kaydet (1 Saat sakla)
                        if (resultList.Count > 0)
                        {
                            _memoryCache.Set(cacheKey, resultList, TimeSpan.FromHours(1));
                        }
                        return resultList;
                    }
                    return new List<string>();
                }
                catch
                {
                    currentRetry++;
                    await Task.Delay(2000);
                }
            }
            return new List<string>();
        }

        public async Task<Movie> GetMovieDetails(string movieName)
        {
            // Cache kontrolü: Film detayları sürekli değişmez, onları da cache'leyelim.
            string cacheKey = "Movie_" + movieName.Trim().ToLower();
            if (_memoryCache.TryGetValue(cacheKey, out Movie cachedMovie))
            {
                return cachedMovie;
            }

            try
            {
                // 1. FİLMİ BUL
                var searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={_tmdbApiKey}&query={movieName}&language=tr-TR";
                var response = await _httpClient.GetStringAsync(searchUrl);
                var json = JObject.Parse(response);
                var firstResult = json["results"]?.FirstOrDefault();

                if (firstResult != null)
                {
                    int movieId = (int)firstResult["id"];
                    string posterPath = firstResult["poster_path"]?.ToString();
                    string fullImageUrl = string.IsNullOrEmpty(posterPath) ? "https://via.placeholder.com/300x450?text=Resim+Yok" : $"https://image.tmdb.org/t/p/w500{posterPath}";

                    // 2. FRAGMANI BUL
                    string trailerKey = "";
                    try
                    {
                        var videoUrl = $"https://api.themoviedb.org/3/movie/{movieId}/videos?api_key={_tmdbApiKey}&language=en-US";
                        var videoResponse = await _httpClient.GetStringAsync(videoUrl);
                        var videoJson = JObject.Parse(videoResponse);
                        var trailer = videoJson["results"]?.FirstOrDefault(v => v["type"]?.ToString() == "Trailer" && v["site"]?.ToString() == "YouTube");
                        if (trailer != null) trailerKey = trailer["key"]?.ToString();
                    }
                    catch { }

                    // 3. PLATFORMLARI BUL
                    var providersList = new List<string>();
                    try
                    {
                        var providerUrl = $"https://api.themoviedb.org/3/movie/{movieId}/watch/providers?api_key={_tmdbApiKey}";
                        var providerResponse = await _httpClient.GetStringAsync(providerUrl);
                        var providerJson = JObject.Parse(providerResponse);
                        var trProviders = providerJson["results"]?["TR"]?["flatrate"];

                        if (trProviders != null)
                        {
                            foreach (var p in trProviders)
                            {
                                string logoPath = p["logo_path"]?.ToString();
                                if (!string.IsNullOrEmpty(logoPath))
                                {
                                    providersList.Add($"https://image.tmdb.org/t/p/w200{logoPath}");
                                }
                            }
                        }
                    }
                    catch { }

                    var movie = new Movie
                    {
                        Id = movieId,
                        Title = firstResult["title"]?.ToString(),
                        ImageUrl = fullImageUrl,
                        TrailerKey = trailerKey,
                        WatchProviders = providersList
                    };

                    // Cache'e kaydet (24 Saat sakla)
                    _memoryCache.Set(cacheKey, movie, TimeSpan.FromHours(24));

                    return movie;
                }
            }
            catch { }
            return null;
        }
    }
}