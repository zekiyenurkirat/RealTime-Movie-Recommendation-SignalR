using FilmOnerisiProje.Services;
using Microsoft.AspNetCore.SignalR;

namespace FilmOnerisiProje.Hubs
{
    public class CinemaHub : Hub
    {
        private readonly GameService _gameService;
        private readonly TmdbService _tmdbService;

        public CinemaHub(GameService gameService, TmdbService tmdbService)
        {
            _gameService = gameService;
            _tmdbService = tmdbService;
        }

        // KULLANICI BAĞLANTIYI KOPARIRSA (Sayfa yenileme/kapatma)
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var roomCode = _gameService.GetRoomCodeByUser(Context.ConnectionId);
            if (!string.IsNullOrEmpty(roomCode))
            {
                _gameService.RemoveUserFromRoom(Context.ConnectionId);

                // Odada kalanlara güncel listeyi gönder
                var room = _gameService.GetRoom(roomCode);
                if (room != null)
                {
                    await SendRoomStatus(roomCode); // Durum güncellemesi

                    // Eğer kalan herkes oylamayı zaten bitirdiyse sonucu açıkla!
                    if (room.IsGameStarted && room.FinishedVoters.Count >= room.Users.Count && room.Users.Count > 0)
                    {
                        await FinishVoting(roomCode);
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinRoom(string roomCode, string userName)
        {
            var room = _gameService.GetRoom(roomCode) ?? _gameService.CreateRoom(roomCode);
            var newUser = new User { ConnectionId = Context.ConnectionId, Name = userName };
            _gameService.AddUserToRoom(roomCode, newUser);

            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

            // Sadece isimleri değil, durumları da gönderelim
            await SendRoomStatus(roomCode);
        }

        public async Task SubmitUserMovie(string roomCode, string movieName)
        {
            var room = _gameService.GetRoom(roomCode);
            if (room != null)
            {
                room.CollectedMovies.Add(movieName);
                room.UsersWhoSubmitted.Add(Context.ConnectionId); // Bu kişi film girdi işaretle

                await SendRoomStatus(roomCode); // Herkese "Biri film girdi" diye güncelle
            }
        }

        public async Task StartAnalysisAndVoting(string roomCode)
        {
            var room = _gameService.GetRoom(roomCode);
            if (room == null) return;

            // --- 1. YENİ KURAL: EN AZ 2 KİŞİ OLMALI ---
            if (room.Users.Count < 2)
            {
                // Kullanıcıya hata mesajı gönder ve durdur.
                await Clients.Caller.SendAsync("ShowError", "Oyunun başlaması için odada en az 2 kişi olmalı!");
                return;
            }
            // -------------------------------------------

            // --- 2. KURAL: ODADAKİ HERKES FİLM GİRMİŞ OLMALI ---
            // (Odadaki kişi sayısı > Film giren kişi sayısı ise BAŞLATMA)
            if (room.UsersWhoSubmitted.Count < room.Users.Count)
            {
                await Clients.Caller.SendAsync("ShowError", "Herkes henüz film önerisi yapmadı! Bekleniyor...");
                return;
            }

            // Eğer oyun zaten başladıysa sonradan gelene listeyi at
            if (room.IsGameStarted && room.Movies != null && room.Movies.Count > 0)
            {
                await Clients.Caller.SendAsync("StartVoting", room.Movies);
                return;
            }

            if (room.IsGameStarted) return;
            if (!room.CollectedMovies.Any()) return;

            room.IsGameStarted = true;
            room.FinishedVoters.Clear();

            // Gemini işlemleri...
            var recommendedTitles = await _tmdbService.GetGeminiRecommendations(room.CollectedMovies);
            var fullMovies = new List<Movie>();

            foreach (var title in recommendedTitles)
            {
                var movieObj = await _tmdbService.GetMovieDetails(title);
                if (movieObj != null) fullMovies.Add(movieObj);
            }

            room.Movies = fullMovies;
            await Clients.Group(roomCode).SendAsync("StartVoting", fullMovies);
        }

        public async Task CastVote(string roomCode, int movieId, bool isLike)
        {
            var room = _gameService.GetRoom(roomCode);
            if (room != null && isLike)
            {
                var movie = room.Movies.FirstOrDefault(m => m.Id == movieId);
                if (movie != null) movie.LikeCount++;
            }
        }

        public async Task FinishVoting(string roomCode)
        {
            var room = _gameService.GetRoom(roomCode);
            if (room == null) return;

            room.FinishedVoters.Add(Context.ConnectionId);

            // KONTROL: Bitiren sayısı >= Odadaki CANLI kullanıcı sayısı
            if (room.FinishedVoters.Count >= room.Users.Count)
            {
                var winner = room.Movies.OrderByDescending(m => m.LikeCount).FirstOrDefault();
                if (winner != null)
                {
                    await Clients.Group(roomCode).SendAsync("ShowFinalResult", winner);
                }

                // Sıfırla
                room.IsGameStarted = false;
                room.CollectedMovies.Clear();
                room.FinishedVoters.Clear();
                room.UsersWhoSubmitted.Clear();
                room.Movies.Clear();
            }
        }

        // YARDIMCI METOD: Odaya kimlerin film girdiğini bildirir
        private async Task SendRoomStatus(string roomCode)
        {
            var room = _gameService.GetRoom(roomCode);
            if (room != null)
            {
                // Örn: "Ahmet (Hazır)", "Mehmet (Bekleniyor)"
                var statusList = room.Users.Select(u =>
                    room.UsersWhoSubmitted.Contains(u.ConnectionId)
                    ? $"{u.Name} ✅"
                    : $"{u.Name} ⏳"
                ).ToList();

                await Clients.Group(roomCode).SendAsync("UpdateUserList", statusList);
            }
        }
    }
}