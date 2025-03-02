<script>
  import { createEventDispatcher } from 'svelte';
  import { slide } from 'svelte/transition';
  
  export let items = [];
  export let level = 0;
  export let selectedItem = null;
  
  const dispatch = createEventDispatcher();
  
  function handleClick(item, event) {
    if (event.type === 'contextmenu') {
      // Prevent default context menu
      event.preventDefault();
      // Select the item but don't open it
      dispatch('itemClick', item);
      return;
    }

    // For left click
    dispatch('itemClick', item);
    
    if (item.isDir) {
      handleToggle(item, event);
    } else {
      // For files, dispatch select event
      dispatch('select', {
        path: item.path,
        name: item.name,
        icon: item.icon,
        isDir: item.isDir
      });
    }
  }
  
  function handleToggle(item, event) {
    event.stopPropagation();
    item.expanded = !item.expanded;
    dispatch('toggle', { item, expanded: item.expanded });
  }
</script>

<ul class="tree-view">
  {#each items as item}
    <li class="tree-item">
      <div 
        class="item-content" 
        style="padding-left: {level * 16}px"
        class:selected={item === selectedItem}
        on:click={(e) => handleClick(item, e)}
        on:contextmenu={(e) => handleClick(item, e)}>
        {#if item.isDir}
          <span class="expander" on:click|stopPropagation={(e) => handleToggle(item, e)}>
            {item.expanded ? '▼' : '▶'}
          </span>
        {:else}
          <span class="expander-placeholder"></span>
        {/if}
        <span class="icon">{item.icon}</span>
        <span class="name">{item.name}</span>
      </div>
      {#if item.isDir && item.expanded && item.children}
        <svelte:self 
          items={item.children} 
          {selectedItem}
          level={level + 1}
          on:select 
          on:toggle 
          on:itemClick 
        />
      {/if}
    </li>
  {/each}
</ul>

<style>
  .tree-view {
    list-style: none;
    padding: 0;
    margin: 0;
  }

  .tree-item {
    margin: 0.25rem 0;
  }

  .item-content {
    display: flex;
    align-items: center;
    padding: 0.25rem;
    cursor: pointer;
    border-radius: 4px;
    user-select: none;
    color: var(--text-color);
  }

  .item-content:hover {
    background-color: var(--hover-color);
  }

  .item-content.selected {
    background-color: var(--selection-color);
  }

  .expander, .expander-placeholder {
    width: 20px;
    text-align: center;
    font-size: 0.8rem;
    color: var(--disabled-text);
  }

  .icon {
    margin-right: 0.5rem;
  }

  .name {
    flex: 1;
  }
</style> 