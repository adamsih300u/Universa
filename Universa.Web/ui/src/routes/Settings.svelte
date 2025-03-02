<script>
  let settings = {
    username: '',
    password: '',
    syncEnabled: false,
    aiServices: {
      openai: {
        enabled: false,
        apiKey: ''
      },
      anthropic: {
        enabled: false,
        apiKey: ''
      },
      xai: {
        enabled: false,
        apiKey: ''
      }
    }
  };

  async function loadSettings() {
    try {
      const response = await fetch('/api/settings');
      if (response.ok) {
        const data = await response.json();
        settings = {
          ...settings,
          ...data,
          // Ensure sensitive data is not overwritten if not returned by API
          aiServices: {
            ...settings.aiServices,
            ...data.aiServices,
            openai: {
              ...settings.aiServices.openai,
              ...(data.aiServices?.openai || {}),
              apiKey: data.aiServices?.openai?.apiKey || settings.aiServices.openai.apiKey
            },
            anthropic: {
              ...settings.aiServices.anthropic,
              ...(data.aiServices?.anthropic || {}),
              apiKey: data.aiServices?.anthropic?.apiKey || settings.aiServices.anthropic.apiKey
            },
            xai: {
              ...settings.aiServices.xai,
              ...(data.aiServices?.xai || {}),
              apiKey: data.aiServices?.xai?.apiKey || settings.aiServices.xai.apiKey
            }
          }
        };
      }
    } catch (error) {
      console.error('Error loading settings:', error);
    }
  }

  async function saveSettings() {
    try {
      const response = await fetch('/api/settings', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(settings)
      });

      if (response.ok) {
        alert('Settings saved successfully!');
      }
    } catch (error) {
      console.error('Error saving settings:', error);
      alert('Failed to save settings');
    }
  }

  // Load settings when component mounts
  import { onMount } from 'svelte';
  onMount(loadSettings);
</script>

<div class="settings-container">
  <h2>Settings</h2>

  <form on:submit|preventDefault={saveSettings}>
    <section class="settings-section">
      <h3>User Settings</h3>
      <div class="form-group">
        <label for="username">Username</label>
        <input type="text" id="username" bind:value={settings.username}>
      </div>

      <div class="form-group">
        <label for="password">Password</label>
        <input type="password" id="password" bind:value={settings.password}>
      </div>

      <div class="form-group">
        <label class="checkbox-label">
          <input type="checkbox" bind:checked={settings.syncEnabled}>
          Enable Synchronization
        </label>
      </div>
    </section>

    <section class="settings-section">
      <h3>AI Services</h3>
      
      <!-- OpenAI -->
      <div class="service-config">
        <div class="service-header">
          <h4>OpenAI</h4>
          <label class="toggle-switch">
            <input type="checkbox" bind:checked={settings.aiServices.openai.enabled}>
            <span class="slider"></span>
          </label>
        </div>
        <div class="form-group" class:disabled={!settings.aiServices.openai.enabled}>
          <label for="openai-key">API Key</label>
          <input 
            type="password" 
            id="openai-key" 
            bind:value={settings.aiServices.openai.apiKey}
            disabled={!settings.aiServices.openai.enabled}
          >
        </div>
      </div>

      <!-- Anthropic -->
      <div class="service-config">
        <div class="service-header">
          <h4>Anthropic</h4>
          <label class="toggle-switch">
            <input type="checkbox" bind:checked={settings.aiServices.anthropic.enabled}>
            <span class="slider"></span>
          </label>
        </div>
        <div class="form-group" class:disabled={!settings.aiServices.anthropic.enabled}>
          <label for="anthropic-key">API Key</label>
          <input 
            type="password" 
            id="anthropic-key" 
            bind:value={settings.aiServices.anthropic.apiKey}
            disabled={!settings.aiServices.anthropic.enabled}
          >
        </div>
      </div>

      <!-- xAI -->
      <div class="service-config">
        <div class="service-header">
          <h4>xAI</h4>
          <label class="toggle-switch">
            <input type="checkbox" bind:checked={settings.aiServices.xai.enabled}>
            <span class="slider"></span>
          </label>
        </div>
        <div class="form-group" class:disabled={!settings.aiServices.xai.enabled}>
          <label for="xai-key">API Key</label>
          <input 
            type="password" 
            id="xai-key" 
            bind:value={settings.aiServices.xai.apiKey}
            disabled={!settings.aiServices.xai.enabled}
          >
        </div>
      </div>
    </section>

    <div class="form-actions">
      <button type="submit" class="save-button">Save Settings</button>
    </div>
  </form>
</div>

<style>
  .settings-container {
    max-width: 800px;
    margin: 0 auto;
    padding: 2rem;
  }

  h2 {
    color: var(--heading-color, #2c3e50);
    margin-bottom: 2rem;
  }

  h3 {
    color: var(--subheading-color, #34495e);
    margin: 1rem 0;
  }

  .settings-section {
    background: var(--section-bg, white);
    border: 1px solid var(--border-color, #eee);
    border-radius: 8px;
    padding: 1.5rem;
    margin-bottom: 2rem;
  }

  .form-group {
    margin-bottom: 1rem;
  }

  .form-group.disabled {
    opacity: 0.5;
  }

  label {
    display: block;
    margin-bottom: 0.5rem;
    color: var(--label-color, #666);
    font-weight: 500;
  }

  .checkbox-label {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    cursor: pointer;
  }

  input[type="text"],
  input[type="password"] {
    width: 100%;
    padding: 0.5rem;
    border: 1px solid var(--input-border, #ddd);
    border-radius: 4px;
    font-size: 1rem;
  }

  .service-config {
    background: var(--service-bg, #f8f9fa);
    border: 1px solid var(--border-color, #eee);
    border-radius: 6px;
    padding: 1rem;
    margin-bottom: 1rem;
  }

  .service-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 1rem;
  }

  .service-header h4 {
    margin: 0;
    color: var(--service-heading, #2c3e50);
  }

  .toggle-switch {
    position: relative;
    display: inline-block;
    width: 48px;
    height: 24px;
  }

  .toggle-switch input {
    opacity: 0;
    width: 0;
    height: 0;
  }

  .slider {
    position: absolute;
    cursor: pointer;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background-color: #ccc;
    transition: .4s;
    border-radius: 24px;
  }

  .slider:before {
    position: absolute;
    content: "";
    height: 18px;
    width: 18px;
    left: 3px;
    bottom: 3px;
    background-color: white;
    transition: .4s;
    border-radius: 50%;
  }

  input:checked + .slider {
    background-color: var(--primary-color, #007bff);
  }

  input:checked + .slider:before {
    transform: translateX(24px);
  }

  .form-actions {
    display: flex;
    justify-content: flex-end;
    margin-top: 2rem;
  }

  .save-button {
    background-color: var(--primary-color, #007bff);
    color: white;
    border: none;
    padding: 0.75rem 1.5rem;
    border-radius: 4px;
    cursor: pointer;
    font-size: 1rem;
    transition: background-color 0.2s;
  }

  .save-button:hover {
    background-color: var(--primary-hover-color, #0056b3);
  }
</style> 