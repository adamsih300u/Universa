<script>
  import { onMount, onDestroy, createEventDispatcher } from 'svelte';
  import { websocketService } from '../services/websocket-service';

  const dispatch = createEventDispatcher();

  export let content = '';
  export let path = '';
  export let readOnly = false;

  let editor;
  let isDirty = false;
  let autoSaveInterval;
  let wordCount = 0;
  let charCount = 0;
  let readingTime = '0 min';
  let currentContent = content;

  function updateStats(text) {
    // Character count (excluding whitespace)
    charCount = text.replace(/\s/g, '').length;
    
    // Word count
    wordCount = text.trim().split(/\s+/).filter(word => word.length > 0).length;
    
    // Reading time (average reading speed: 200 words per minute)
    const minutes = Math.ceil(wordCount / 200);
    readingTime = `${minutes} min`;
  }

  $: {
    // Update stats whenever content changes
    updateStats(currentContent);
  }

  onMount(() => {
    // Initial stats calculation
    updateStats(currentContent);
    
    // Set up auto-save
    autoSaveInterval = setInterval(autoSave, 30000); // Auto-save every 30 seconds
  });

  onDestroy(() => {
    if (autoSaveInterval) {
      clearInterval(autoSaveInterval);
    }
  });

  async function autoSave() {
    if (isDirty) {
      await saveContent();
    }
  }

  async function saveContent() {
    try {
      const response = await fetch(`/api/files/${encodeURIComponent(path)}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'text/plain',
          'X-Client': 'web'
        },
        body: currentContent
      });

      if (response.ok) {
        isDirty = false;
        dispatch('dirtyStateChange', { isDirty: false });
        dispatch('contentUpdate', { content: currentContent, saved: true });
        
        // Notify other clients about the file change using the WebSocket service
        websocketService.notifyFileChange(path);
        
        console.log('File saved successfully');
      } else {
        const errorText = await response.text();
        console.error('Failed to save file:', errorText);
        alert(`Failed to save file: ${errorText}`);
      }
    } catch (error) {
      console.error('Error saving file:', error);
      alert(`Error saving file: ${error.message}`);
    }
  }

  function handleInput(event) {
    currentContent = event.target.value;
    updateStats(currentContent);
    if (!isDirty) {
      isDirty = true;
      dispatch('dirtyStateChange', { isDirty: true });
    }
    dispatch('contentUpdate', { content: currentContent, saved: false });
  }

  async function handleKeyDown(event) {
    // Ctrl/Cmd + S to save
    if ((event.ctrlKey || event.metaKey) && event.key === 's') {
      event.preventDefault();
      await saveContent();
    }
  }
</script>

<div class="editor-wrapper">
  <div class="stats-bar">
    <div class="stat">
      <span class="stat-label">Words:</span>
      <span class="stat-value">{wordCount}</span>
    </div>
    <div class="separator">|</div>
    <div class="stat">
      <span class="stat-label">Characters:</span>
      <span class="stat-value">{charCount}</span>
    </div>
    <div class="separator">|</div>
    <div class="stat">
      <span class="stat-label">Reading Time:</span>
      <span class="stat-value">{readingTime}</span>
    </div>
  </div>
  <div class="editor-container">
    <textarea
      bind:this={editor}
      value={currentContent}
      on:input={handleInput}
      on:keydown={handleKeyDown}
      placeholder="Start writing..."
      disabled={readOnly}
      spellcheck="false"
    ></textarea>
  </div>
</div>

<style>
  .editor-wrapper {
    display: flex;
    flex-direction: column;
    height: 100%;
  }

  .stats-bar {
    display: flex;
    align-items: center;
    padding: 4px 20px;
    background-color: var(--section-bg);
    border-bottom: 1px solid var(--border-color);
    font-size: 12px;
    color: var(--text-color);
    height: 24px;
  }

  .stat {
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .stat-label {
    color: var(--disabled-text);
  }

  .stat-value {
    font-weight: 500;
    color: var(--text-color);
  }

  .separator {
    margin: 0 12px;
    color: var(--border-color);
  }

  .editor-container {
    flex: 1;
    background-color: var(--background-color);
    padding: 20px;
    height: calc(100% - 29px); /* 29px = stats-bar height + border */
  }

  textarea {
    width: 100%;
    height: 100%;
    border: none;
    resize: none;
    font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
    font-size: 14px;
    line-height: 1.6;
    padding: 0;
    outline: none;
    background-color: var(--background-color);
    color: var(--text-color);
  }

  textarea:focus {
    outline: none;
  }
</style> 