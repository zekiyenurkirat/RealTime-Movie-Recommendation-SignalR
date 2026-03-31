using System.Collections.Concurrent;

namespace FilmOnerisiProje.Services
{
    // ... Room ve GameService sınıfları AYNI KALSIN (Değişiklik yok) ...
    // Sadece Movie sınıfını güncelle:

    public class Movie
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ImageUrl { get; set; }
        public int LikeCount { get; set; } = 0;

        // YENİ: Fragman YouTube ID'si (örn: d96cjJhvlMA)
        public string TrailerKey { get; set; }

        // YENİ: İzlenebilecek Platformların Logoları
        public List<string> WatchProviders { get; set; } = new List<string>();
    }

    // ... Room, User ve GameService sınıflarını olduğu gibi koru ...
    public class Room { public string RoomCode { get; set; } public List<User> Users { get; set; } = new List<User>(); public List<Movie> Movies { get; set; } = new List<Movie>(); public List<string> CollectedMovies { get; set; } = new List<string>(); public HashSet<string> FinishedVoters { get; set; } = new HashSet<string>(); public HashSet<string> UsersWhoSubmitted { get; set; } = new HashSet<string>(); public bool IsGameStarted { get; set; } = false; }
    public class User { public string ConnectionId { get; set; } public string Name { get; set; } }

    public class GameService
    {
        private readonly ConcurrentDictionary<string, Room> _rooms = new();
        public Room GetRoom(string code) { _rooms.TryGetValue(code, out var room); return room; }
        public Room CreateRoom(string code) { var room = new Room { RoomCode = code }; _rooms.TryAdd(code, room); return room; }
        public void AddUserToRoom(string code, User user) { if (_rooms.TryGetValue(code, out var room)) { if (!room.Users.Any(u => u.ConnectionId == user.ConnectionId)) { room.Users.Add(user); } } }
        public void RemoveUserFromRoom(string connectionId) { foreach (var room in _rooms.Values) { var user = room.Users.FirstOrDefault(u => u.ConnectionId == connectionId); if (user != null) { room.Users.Remove(user); room.FinishedVoters.Remove(connectionId); room.UsersWhoSubmitted.Remove(connectionId); if (room.Users.Count == 0) { _rooms.TryRemove(room.RoomCode, out _); } break; } } }
        public string GetRoomCodeByUser(string connectionId) { foreach (var room in _rooms.Values) { if (room.Users.Any(u => u.ConnectionId == connectionId)) { return room.RoomCode; } } return null; }
    }
}