<script>
  import { theme } from '../stores/theme';
  import { createEventDispatcher } from 'svelte';

  const dispatch = createEventDispatcher();

  function handleClose() {
    dispatch('close');
  }
</script>

<div class="settings-container">
  <div class="settings-header">
    <h2>Settings</h2>
    <button class="close-button" on:click={handleClose}>×</button>
  </div>

  <div class="settings-content">
    <section>
      <h3>Appearance</h3>
      <div class="setting-item">
        <label for="theme">Theme</label>
        <select id="theme" bind:value={$theme}>
          <option value="light">Light</option>
          <option value="dark">Dark</option>
        </select>
      </div>
    </section>

    <section>
      <h3>Editor</h3>
      <div class="setting-item">
        <label>
          <input type="checkbox" checked> Auto-save
        </label>
      </div>
      <div class="setting-item">
        <label>
          <input type="checkbox" checked> Line numbers
        </label>
      </div>
      <div class="setting-item">
        <label for="font-size">Font Size</label>
        <select id="font-size">
          <option value="12">12px</option>
          <option value="14">14px</option>
          <option value="16">16px</option>
          <option value="18">18px</option>
        </select>
      </div>
    </section>

    <section>
      <h3>About</h3>
      <div class="about-info">
        <p>Universa Editor</p>
        <p class="version">Version 1.0.0</p>
      </div>
    </section>
  </div>
</div>

<style>
  .settings-container {
    background: var(--section-bg);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    width: 100%;
    height: 100%;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .settings-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 1rem;
    border-bottom: 1px solid var(--border-color);
    background: var(--tab-bg);
  }

  .settings-header h2 {
    margin: 0;
    font-size: 1.25rem;
    color: var(--text-color);
  }

  .close-button {
    background: none;
    border: none;
    font-size: 1.5rem;
    color: var(--text-color);
    cursor: pointer;
    padding: 0.25rem 0.5rem;
    border-radius: 4px;
  }

  .close-button:hover {
    background: var(--hover-color);
  }

  .settings-content {
    flex: 1;
    overflow-y: auto;
    padding: 1rem;
  }

  section {
    margin-bottom: 2rem;
  }

  section h3 {
    font-size: 1rem;
    color: var(--text-color);
    margin: 0 0 1rem 0;
    padding-bottom: 0.5rem;
    border-bottom: 1px solid var(--border-color);
  }

  .setting-item {
    margin-bottom: 1rem;
    display: flex;
    align-items: center;
    justify-content: space-between;
    color: var(--text-color);
  }

  .setting-item label {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  select {
    background: var(--section-bg);
    border: 1px solid var(--border-color);
    color: var(--text-color);
    padding: 0.25rem 0.5rem;
    border-radius: 4px;
  }

  .about-info {
    color: var(--text-color);
  }

  .version {
    color: var(--disabled-text);
    font-size: 0.875rem;
  }
</style> 