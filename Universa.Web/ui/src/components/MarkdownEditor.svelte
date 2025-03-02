<script>
  import { onMount, onDestroy } from 'svelte';
  import { marked } from 'marked';

  export let content = '';
  export let path = '';
  export let readOnly = false;

  let editor;
  let preview;
  let isDirty = false;
  let autoSaveInterval;

  $: previewHtml = marked(content);

  onMount(() => {
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
        },
        body: content,
      });

      if (response.ok) {
        isDirty = false;
      } else {
        console.error('Failed to save file');
      }
    } catch (error) {
      console.error('Error saving file:', error);
    }
  }

  function handleInput(event) {
    content = event.target.value;
    isDirty = true;
  }

  async function handleKeyDown(event) {
    // Ctrl/Cmd + S to save
    if ((event.ctrlKey || event.metaKey) && event.key === 's') {
      event.preventDefault();
      await saveContent();
    }
  }
</script>

<div class="editor-container">
  <div class="editor-section">
    <textarea
      bind:this={editor}
      value={content}
      on:input={handleInput}
      on:keydown={handleKeyDown}
      placeholder="Start writing..."
      disabled={readOnly}
    ></textarea>
  </div>
  <div class="preview-section" bind:this={preview}>
    {@html previewHtml}
  </div>
</div>

<style>
  .editor-container {
    display: flex;
    height: 100%;
    gap: 1px;
    background-color: #dee2e6;
  }

  .editor-section, .preview-section {
    flex: 1;
    overflow: auto;
    background-color: #fff;
    padding: 20px;
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
  }

  .preview-section {
    padding: 20px;
    line-height: 1.6;
  }

  .preview-section :global(h1) {
    font-size: 2em;
    margin-bottom: 0.5em;
  }

  .preview-section :global(h2) {
    font-size: 1.5em;
    margin-bottom: 0.5em;
  }

  .preview-section :global(h3) {
    font-size: 1.2em;
    margin-bottom: 0.5em;
  }

  .preview-section :global(p) {
    margin-bottom: 1em;
  }

  .preview-section :global(ul), .preview-section :global(ol) {
    margin-bottom: 1em;
    padding-left: 2em;
  }

  .preview-section :global(code) {
    background-color: #f8f9fa;
    padding: 0.2em 0.4em;
    border-radius: 3px;
    font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  }

  .preview-section :global(pre code) {
    display: block;
    padding: 1em;
    overflow-x: auto;
  }

  .preview-section :global(blockquote) {
    border-left: 4px solid #dee2e6;
    padding-left: 1em;
    margin-left: 0;
    margin-bottom: 1em;
    color: #666;
  }
</style> 