package middleware

import (
    "context"
    "net/http"
    "universa.web/session"
    "universa.web/config"
    "log"
)

func BasicAuth(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        var username string
        var ok bool

        // Try session auth first
        sess, err := session.GetSession(r)
        if err == nil {
            username, ok = sess.Values["username"].(string)
            log.Printf("Session auth result: ok=%v, username=%s", ok, username)
        }

        // If session auth fails, try basic auth
        if !ok || username == "" {
            username, password, hasAuth := r.BasicAuth()
            log.Printf("Basic auth provided: %v", hasAuth)
            
            if !hasAuth || !config.ValidateCredentials(username, password) {
                log.Printf("ERROR: Basic auth validation failed")
                http.Error(w, "Unauthorized", http.StatusUnauthorized)
                return
            }
            log.Printf("Basic auth successful for user: %s", username)
            ok = true
        }

        if !ok || username == "" {
            log.Printf("ERROR: No valid authentication found")
            http.Error(w, "Unauthorized", http.StatusUnauthorized)
            return
        }

        // Add username to context
        ctx := context.WithValue(r.Context(), "username", username)
        r = r.WithContext(ctx)

        next.ServeHTTP(w, r)
    })
} 