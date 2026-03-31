using FilmOnerisiProje.Hubs;
using FilmOnerisiProje.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC Servisleri
builder.Services.AddMemoryCache(); // <--- Bunu ekle
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameService>();
builder.Services.AddHttpClient<TmdbService>();

// *** EKLENECEK KISIMLAR ***
builder.Services.AddSignalR(); // SignalR'ý ekle
builder.Services.AddSingleton<GameService>(); // Oyun servisini tekil olarak ekle
// **************************

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// *** EKLENECEK ROUTE ***
app.MapHub<CinemaHub>("/cinemaHub"); // Hub adresini belirle
// ***********************

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();