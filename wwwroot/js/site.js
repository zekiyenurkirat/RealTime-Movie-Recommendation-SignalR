var connection = new signalR.HubConnectionBuilder().withUrl("/cinemaHub").build();
var currentRoom = "";
var movies = [];
var currentIndex = 0;

connection.start().catch(err => console.error(err));

async function joinRoom() {
    const user = document.getElementById("userInput").value;
    currentRoom = document.getElementById("roomInput").value;
    if (!user || !currentRoom) return alert("Bilgileri giriniz");
    await connection.invoke("JoinRoom", currentRoom, user);
    document.getElementById("loginSection").style.display = "none";
    document.getElementById("suggestSection").style.display = "block";
}

async function submitMovie() {
    const movieName = document.getElementById("favMovieInput").value;
    if (!movieName) return alert("Film yazınız");
    await connection.invoke("SubmitUserMovie", currentRoom, movieName);
    document.getElementById("btnSubmitMovie").disabled = true;
    document.getElementById("favMovieInput").disabled = true;
    document.getElementById("waitText").style.display = "block";
}

async function startAnalysis() {
    const btn = document.getElementById("btnStartGame");
    // Eğer buton disable ise tıklatmayalım (Güvenlik)
    if (btn.disabled) return alert("Herkesin film girmesini bekleyin!");

    btn.innerText = "Yapay Zeka Çalışıyor...";
    btn.disabled = true;
    await connection.invoke("StartAnalysisAndVoting", currentRoom);
}

// HATA MESAJI (Backend'den gelirse)
connection.on("ShowError", message => {
    alert("⚠️ " + message);
    const btn = document.getElementById("btnStartGame");
    btn.innerText = "Analiz Et ve Başla 🚀";
    btn.disabled = false; // Hata varsa tekrar aç
});

// KULLANICI LİSTESİ GÜNCELLENİNCE BUTON KONTROLÜ
connection.on("UpdateUserList", userStatusList => {
    const ul1 = document.getElementById("userList");
    if (ul1) ul1.innerHTML = userStatusList.join("<br>");

    // Status göstergesi
    let statusDiv = document.getElementById("roomStatusDisplay");
    if (!statusDiv) {
        statusDiv = document.createElement("div");
        statusDiv.id = "roomStatusDisplay";
        statusDiv.style.marginTop = "15px";
        statusDiv.style.color = "#ccc";
        statusDiv.style.fontSize = "0.9rem";
        document.getElementById("suggestSection").appendChild(statusDiv);
    }
    statusDiv.innerHTML = "<strong>Durum:</strong><br>" + userStatusList.join("<br>");

    // KONTROL: Herkesin yanında ✅ var mı?
    // userStatusList string dizisidir: ["Ahmet ✅", "Mehmet ⏳"]
    const allReady = userStatusList.every(s => s.includes("✅"));
    const btn = document.getElementById("btnStartGame");

    if (allReady) {
        btn.disabled = false;
        btn.style.opacity = "1";
        btn.style.cursor = "pointer";
        btn.innerText = "Analiz Et ve Başla 🚀";
    } else {
        btn.disabled = true;
        btn.style.opacity = "0.5";
        btn.style.cursor = "not-allowed";
        btn.innerText = "Herkesin Bitirmesi Bekleniyor...";
    }
});

// DİĞER FONKSİYONLAR AYNI...
async function handleVote(liked) {
    if (currentIndex >= movies.length) return;
    const card = document.getElementById(`card-${currentIndex}`);
    if (card) {
        card.classList.add(liked ? "swipe-right" : "swipe-left");
        setTimeout(() => { card.style.display = "none"; }, 300);
    }
    await connection.invoke("CastVote", currentRoom, movies[currentIndex].id, liked);
    currentIndex++;
    if (currentIndex >= movies.length) {
        document.getElementById("votingSection").style.display = "none";
        document.getElementById("waitingOthersSection").style.display = "block";
        await connection.invoke("FinishVoting", currentRoom);
    }
}

function openTrailer(key) {
    if (!key) return alert("Fragman bulunamadı!");
    const frame = document.getElementById("trailerFrame");
    frame.src = "https://www.youtube.com/embed/" + key + "?autoplay=1";
    var myModal = new bootstrap.Modal(document.getElementById('trailerModal'));
    myModal.show();
}
function closeTrailer() { document.getElementById("trailerFrame").src = ""; }

connection.on("StartVoting", incomingMovies => {
    movies = incomingMovies;
    currentIndex = 0;
    document.getElementById("suggestSection").style.display = "none";
    document.getElementById("waitingOthersSection").style.display = "none";
    document.getElementById("resultSection").style.display = "none";
    document.getElementById("votingSection").style.display = "block";

    const stack = document.getElementById("cardStack");
    stack.innerHTML = "";

    for (let i = movies.length - 1; i >= 0; i--) {
        let m = movies[i];
        let bg = m.imageUrl ? `background-image: url('${m.imageUrl}');` : "background-color: #333;";

        let trailerButton = "";
        if (m.trailerKey) {
            trailerButton = `<button class="btn btn-danger rounded-circle position-absolute top-50 start-50 translate-middle shadow-lg" style="width:60px;height:60px;font-size:24px;z-index:99;" onclick="event.stopPropagation(); openTrailer('${m.trailerKey}')"><i class="fas fa-play"></i></button>`;
        }

        let html = `
            <div class="movie-card" id="card-${i}" style="${bg} z-index:${movies.length - i};">
                ${trailerButton}
                <div class="movie-info"><h3>${m.title}</h3></div>
            </div>`;
        stack.innerHTML += html;
    }
});

connection.on("ShowFinalResult", movie => {
    document.getElementById("votingSection").style.display = "none";
    document.getElementById("waitingOthersSection").style.display = "none";
    document.getElementById("resultSection").style.display = "block";
    document.getElementById("resultTitle").innerText = movie.title;
    document.getElementById("resultImg").src = movie.imageUrl;

    // FRAGMAN BUTONU (Zorla Göster)
    const playBtn = document.getElementById("resultPlayBtn");
    if (movie.trailerKey) {
        playBtn.style.display = "block";
        playBtn.onclick = function () { openTrailer(movie.trailerKey); };
    } else {
        playBtn.style.display = "none";
    }

    // PLATFORMLAR
    const logoDiv = document.getElementById("providerLogos");
    logoDiv.innerHTML = "";
    if (movie.watchProviders && movie.watchProviders.length > 0) {
        movie.watchProviders.forEach(url => {
            logoDiv.innerHTML += `<img src="${url}" class="rounded shadow-sm" style="width:45px; height:45px;" title="Burada izle">`;
        });
        document.getElementById("platformsDiv").style.display = "block";
    } else {
        document.getElementById("platformsDiv").style.display = "none";
    }
});