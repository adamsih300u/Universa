<script>
  import { onMount } from 'svelte';
  import { slide } from 'svelte/transition';

  export let isCollapsed = false;
  export let sidebarWidth = 300;

  let selectedModel = 'gpt-4';
  let messages = [];
  let inputText = '';
  let useContext = false;
  let isDragging = false;
  let startX;
  let startWidth;

  const models = [
    { id: 'gpt-4', name: 'GPT-4' },
    { id: 'gpt-3.5-turbo', name: 'GPT-3.5 Turbo' },
    { id: 'claude-3-opus', name: 'Claude 3 Opus' },
    { id: 'claude-3-sonnet', name: 'Claude 3 Sonnet' }
  ];

  function handleModelChange(event) {
    selectedModel = event.target.value;
  }

  function handleSubmit() {
    if (!inputText.trim()) return;
    
    messages = [...messages, {
      role: 'user',
      content: inputText,
      timestamp: new Date()
    }];
    
    inputText = '';
    // TODO: Implement actual API call to send message
  }

  function handleKeydown(event) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      handleSubmit();
    }
  }

  function startResize(event) {
    isDragging = true;
    startX = event.clientX;
    startWidth = sidebarWidth;
    window.addEventListener('mousemove', handleResize);
    window.addEventListener('mouseup', stopResize);
    document.body.style.cursor = 'ew-resize';
  }

  function handleResize(event) {
    if (!isDragging) return;
    const diff = startX - event.clientX;
    sidebarWidth = Math.max(250, Math.min(600, startWidth + diff));
  }

  function stopResize(event) {
    isDragging = false;
    window.removeEventListener('mousemove', handleResize);
    window.removeEventListener('mouseup', stopResize);
    document.body.style.cursor = '';
  }

  onMount(() => {
    // Load saved width from localStorage if available
    const savedWidth = localStorage.getItem('aiChatSidebarWidth');
    if (savedWidth) {
      sidebarWidth = parseInt(savedWidth);
    }
  });

  $: {
    // Save width to localStorage when it changes
    if (sidebarWidth) {
      localStorage.setItem('aiChatSidebarWidth', sidebarWidth.toString());
    }
  }
</script>

<div 
  class="sidebar-container"
  class:collapsed={isCollapsed}
  style="width: {isCollapsed ? '40px' : sidebarWidth + 'px'}"
>
  <div class="resize-handle" on:mousedown={startResize}></div>
  
  <button 
    class="collapse-button"
    on:click={() => isCollapsed = !isCollapsed}
    title={isCollapsed ? "Expand Chat" : "Collapse Chat"}
  >
    <span class="collapse-icon" class:collapsed={isCollapsed}>
      {isCollapsed ? '◀' : '▶'}
    </span>
  </button>

  {#if !isCollapsed}
    <div class="sidebar-content">
      <!-- Model Selection -->
      <div class="model-selection">
        <select bind:value={selectedModel} on:change={handleModelChange}>
          {#each models as model}
            <option value={model.id}>{model.name}</option>
          {/each}
        </select>
      </div>

      <!-- Chat History -->
      <div class="chat-history">
        {#each messages as message}
          <div class="message {message.role}">
            <div class="message-content">{message.content}</div>
            <div class="message-timestamp">
              {new Date(message.timestamp).toLocaleTimeString()}
            </div>
          </div>
        {/each}
      </div>

      <!-- Input Area -->
      <div class="input-area">
        <textarea
          bind:value={inputText}
          on:keydown={handleKeydown}
          placeholder="Type your message... (Shift+Enter for new line)"
          rows="3"
        ></textarea>
        <div class="input-controls">
          <div class="context-toggle">
            <input 
              type="checkbox"
              bind:checked={useContext}
              id="context-toggle"
            />
            <label for="context-toggle" class="toggle-wrapper">
              <span class="toggle-slider"></span>
              <span class="toggle-label">Include Context</span>
            </label>
          </div>
          <button class="send-button" on:click={handleSubmit}>
            Send
          </button>
        </div>
      </div>
    </div>
  {/if}
</div>

<style>
  .sidebar-container {
    position: relative;
    height: 100%;
    background: var(--background-color, white);
    border-left: 1px solid var(--border-color, #eee);
    transition: width 0.3s ease;
    overflow: hidden;
  }

  .sidebar-container.collapsed {
    width: 40px !important;
  }

  .resize-handle {
    position: absolute;
    left: 0;
    top: 0;
    width: 4px;
    height: 100%;
    cursor: ew-resize;
    background: transparent;
  }

  .resize-handle:hover {
    background: var(--hover-color);
  }

  .collapse-button {
    position: absolute;
    left: 8px;
    top: 8px;
    width: 24px;
    height: 24px;
    background: transparent;
    border: none;
    cursor: pointer;
    padding: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1;
    color: var(--text-color);
  }

  .collapse-button:hover {
    background-color: var(--hover-color);
  }

  .collapse-icon {
    font-size: 12px;
    transition: transform 0.3s ease;
  }

  .collapse-icon.collapsed {
    transform: rotate(180deg);
  }

  .sidebar-content {
    height: 100%;
    display: flex;
    flex-direction: column;
    padding: 8px;
    padding-top: 40px;
    box-sizing: border-box;
  }

  .model-selection {
    margin-bottom: 16px;
  }

  .model-selection select {
    width: 100%;
    padding: 8px;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    background: var(--section-bg);
    color: var(--text-color);
  }

  .model-selection select:focus {
    outline: none;
    border-color: var(--primary-color);
  }

  .chat-history {
    flex: 1;
    overflow-y: auto;
    margin-bottom: 16px;
    padding: 8px;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    min-height: 100px;
    background-color: var(--section-bg);
  }

  .message {
    margin-bottom: 12px;
    padding: 8px;
    border-radius: 4px;
  }

  .message.user {
    background: var(--user-message-bg, #e3f2fd);
    margin-left: 20%;
  }

  .message.assistant {
    background: var(--assistant-message-bg, #f5f5f5);
    margin-right: 20%;
  }

  .message-timestamp {
    font-size: 0.8em;
    color: var(--timestamp-color, #666);
    text-align: right;
    margin-top: 4px;
  }

  .input-area {
    display: flex;
    flex-direction: column;
    gap: 8px;
    margin-top: auto;
    min-height: 120px;
  }

  textarea {
    width: 100%;
    padding: 8px;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    resize: none;
    font-family: inherit;
    min-height: 60px;
    box-sizing: border-box;
    background-color: var(--section-bg);
    color: var(--text-color);
  }

  textarea:focus {
    outline: none;
    border-color: var(--primary-color);
  }

  .input-controls {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 0;
    width: 100%;
    min-height: 40px;
  }

  .context-toggle {
    position: relative;
    display: flex;
    align-items: center;
    flex: 1;
    margin-right: 8px;
  }

  .toggle-wrapper {
    display: flex;
    align-items: center;
    cursor: pointer;
    white-space: nowrap;
  }

  .toggle-slider {
    position: relative;
    display: inline-block;
    width: 40px;
    height: 20px;
    background: var(--border-color, #ccc);
    border-radius: 20px;
    margin-right: 8px;
    transition: background-color 0.2s;
  }

  .toggle-slider:before {
    content: '';
    position: absolute;
    width: 16px;
    height: 16px;
    border-radius: 50%;
    background: white;
    top: 2px;
    left: 2px;
    transition: transform 0.2s;
  }

  .context-toggle input {
    display: none;
  }

  .context-toggle input:checked + label .toggle-slider {
    background: var(--primary-color, #007bff);
  }

  .context-toggle input:checked + label .toggle-slider:before {
    transform: translateX(20px);
  }

  .toggle-label {
    font-size: 0.9em;
    color: var(--text-color, #666);
    user-select: none;
  }

  .send-button {
    padding: 8px 16px;
    background: var(--primary-color, #007bff);
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    transition: background-color 0.2s;
    white-space: nowrap;
    min-width: 80px;
  }

  .send-button:hover {
    background: var(--primary-hover-color, #0056b3);
  }
</style> 