package session

import (
	"github.com/gorilla/sessions"
	"net/http"
)

var store *sessions.CookieStore

func InitStore(secret []byte) {
	store = sessions.NewCookieStore(secret)
	store.Options = &sessions.Options{
		Path:     "/",
		MaxAge:   86400 * 7, // 7 days
		HttpOnly: true,
		Secure:   false, // Set to true in production with HTTPS
		SameSite: http.SameSiteLaxMode,
	}
}

func GetSession(r *http.Request) (*sessions.Session, error) {
	return store.Get(r, "universa-session")
}

func SaveSession(session *sessions.Session, w http.ResponseWriter, r *http.Request) error {
	return session.Save(r, w)
} 