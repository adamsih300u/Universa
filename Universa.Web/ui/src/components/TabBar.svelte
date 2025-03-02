<script>
  import { createEventDispatcher } from 'svelte';

  export let tabs = [];
  export let activeTab = null;

  const dispatch = createEventDispatcher();

  function handleTabClick(tab) {
    dispatch('tabSelect', tab);
  }

  function handleTabClose(event, tab) {
    event.stopPropagation();
    dispatch('tabClose', tab);
  }
</script>

<div class="tab-bar">
  {#each tabs as tab}
    <div 
      class="tab" 
      class:active={activeTab && activeTab.path === tab.path}
      on:click={() => handleTabClick(tab)}
    >
      <span class="tab-icon">{tab.icon}</span>
      <span class="tab-name">{tab.name}{tab.isDirty ? '*' : ''}</span>
      <button 
        class="close-button" 
        on:click={(e) => handleTabClose(e, tab)}
        title="Close tab"
      >
        ×
      </button>
    </div>
  {/each}
</div>

<style>
  .tab-bar {
    display: flex;
    background-color: var(--tab-bg);
    border-bottom: 1px solid var(--border-color);
    height: 40px;
    overflow-x: auto;
    overflow-y: hidden;
    min-height: 40px;
  }

  .tab {
    display: flex;
    align-items: center;
    padding: 0 16px;
    min-width: 100px;
    max-width: 200px;
    height: 100%;
    border-right: 1px solid var(--border-color);
    background-color: var(--section-bg);
    cursor: pointer;
    user-select: none;
    position: relative;
    gap: 8px;
    color: var(--text-color);
  }

  .tab:hover {
    background-color: var(--hover-color);
  }

  .tab.active {
    background-color: var(--tab-active-bg);
    border-bottom: 2px solid var(--primary-color);
  }

  .tab-icon {
    font-size: 14px;
  }

  .tab-name {
    flex: 1;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    font-size: 14px;
  }

  .close-button {
    background: none;
    border: none;
    padding: 4px;
    font-size: 16px;
    line-height: 1;
    cursor: pointer;
    border-radius: 4px;
    color: var(--disabled-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .close-button:hover {
    background-color: var(--hover-color);
    color: var(--text-color);
  }
</style> 