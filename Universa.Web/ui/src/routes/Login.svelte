<script>
  import { login } from '../stores/auth';

  let username = '';
  let password = '';
  let error = '';
  let isLoading = false;

  async function handleSubmit(event) {
    event.preventDefault();
    if (isLoading) return;

    error = '';
    isLoading = true;

    try {
      const success = await login(username, password);
      if (success) {
        // Force a clean reload of the app
        window.location.href = '/';
      } else {
        error = 'Invalid username or password';
      }
    } finally {
      isLoading = false;
    }
  }
</script>

<div class="login-container">
  <div class="login-header">
    <h1>Universa</h1>
    <p>Sign in to continue</p>
  </div>
  <form class="login-form" on:submit={handleSubmit}>
    <div class="form-group">
      <label for="username">Username</label>
      <input type="text" id="username" bind:value={username} required disabled={isLoading}>
    </div>
    <div class="form-group">
      <label for="password">Password</label>
      <input type="password" id="password" bind:value={password} required disabled={isLoading}>
    </div>
    <button type="submit" class="login-button" disabled={isLoading}>
      {isLoading ? 'Signing in...' : 'Sign In'}
    </button>
    {#if error}
      <div class="error-message">{error}</div>
    {/if}
  </form>
</div>

<style>
  .login-container {
    background: rgba(255, 255, 255, 0.1);
    padding: 2rem;
    border-radius: 0.5rem;
    width: 100%;
    max-width: 400px;
    backdrop-filter: blur(10px);
    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
    margin: auto;
  }

  .login-header {
    text-align: center;
    margin-bottom: 2rem;
  }

  .login-header h1 {
    color: var(--text-color);
    font-size: 2rem;
    margin-bottom: 0.5rem;
  }

  .login-header p {
    color: var(--text-color);
    opacity: 0.8;
  }

  .login-form {
    display: flex;
    flex-direction: column;
    gap: 1rem;
  }

  .form-group {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  .form-group label {
    color: var(--text-color);
    font-size: 0.875rem;
  }

  .form-group input {
    padding: 0.75rem;
    border-radius: 0.375rem;
    border: 1px solid var(--border-color);
    background: var(--section-bg);
    color: var(--text-color);
    font-size: 1rem;
  }

  .form-group input:focus {
    outline: none;
    border-color: var(--primary-color);
  }

  .login-button {
    margin-top: 1rem;
    padding: 0.75rem;
    background: var(--primary-color);
    color: white;
    border: none;
    border-radius: 0.375rem;
    font-size: 1rem;
    cursor: pointer;
    transition: opacity 0.2s;
  }

  .login-button:hover {
    opacity: 0.9;
  }

  .login-button:disabled {
    opacity: 0.7;
    cursor: not-allowed;
  }

  input:disabled {
    opacity: 0.7;
    cursor: not-allowed;
  }

  .error-message {
    color: #ef4444;
    font-size: 0.875rem;
    margin-top: 1rem;
    text-align: center;
  }

  :global(body) {
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 100vh;
    margin: 0;
    background-color: var(--sidebar-bg);
  }
</style> 