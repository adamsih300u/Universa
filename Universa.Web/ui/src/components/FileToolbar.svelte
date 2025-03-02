<script>
  import { createEventDispatcher } from 'svelte';
  
  export let selectedItem = null;
  const dispatch = createEventDispatcher();
  
  function handleNewFolder() {
    dispatch('newFolder');
  }
  
  function handleNewNote() {
    dispatch('newNote');
  }
  
  function handleDelete() {
    if (selectedItem) {
      dispatch('delete', selectedItem);
    }
  }
</script>

<div class="toolbar">
  <button 
    class="toolbar-button" 
    on:click={handleNewFolder} 
    title="New Folder">
    <span class="icon">📁</span>
  </button>
  <button 
    class="toolbar-button" 
    on:click={handleNewNote} 
    title="New Note">
    <span class="icon">📝</span>
  </button>
  <button 
    class="toolbar-button" 
    on:click={handleDelete} 
    disabled={!selectedItem}
    title="Delete Selected">
    <span class="icon">🗑️</span>
  </button>
</div>

<style>
  .toolbar {
    display: flex;
    gap: 0.5rem;
    padding: 0.5rem;
    border-top: 1px solid var(--border-color);
    border-bottom: 1px solid var(--border-color);
    background-color: var(--section-bg);
  }

  .toolbar-button {
    background: none;
    border: none;
    padding: 0.5rem;
    cursor: pointer;
    border-radius: 4px;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: background-color 0.2s;
    color: var(--text-color);
  }

  .toolbar-button:hover:not(:disabled) {
    background-color: var(--hover-color);
  }

  .toolbar-button:disabled {
    opacity: 0.5;
    cursor: not-allowed;
    color: var(--disabled-text);
  }

  .icon {
    font-size: 1.2rem;
    line-height: 1;
  }
</style>