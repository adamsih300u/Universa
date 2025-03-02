<script>
  import { onMount } from 'svelte';
  import FileToolbar from '../components/FileToolbar.svelte';

  export let item = null;
  export let level = 0;

  let files = [];
  let loading = true;
  let error = null;
  let rootItems = [];
  let selectedItem = null;

  // Function to convert flat file list to tree structure
  function buildFileTree(files) {
    const root = {
      name: 'Library',
      path: '',
      type: 'directory',
      icon: '📚',
      children: [],
      isExpanded: true
    };

    // Sort files by path to ensure parent directories come before children
    files.sort((a, b) => (a.path || a.Path).localeCompare(b.path || b.Path));

    files.forEach(file => {
      const path = (file.path || file.Path).replace(/\\/g, '/');
      const parts = path.split('/');
      let currentNode = root;

      for (let i = 0; i < parts.length; i++) {
        const part = parts[i];
        if (!part) continue;

        // Skip .versions directories and hidden files
        if (part === '.versions' || part.startsWith('.')) {
          return; // Skip the entire file/directory if it's in .versions
        }

        const isLastPart = i === parts.length - 1;
        const fullPath = parts.slice(0, i + 1).join('/');
        let node = currentNode.children.find(n => n.name === part);

        if (!node) {
          node = {
            name: part,
            path: fullPath,
            type: isLastPart && !file.isDirectory ? 'file' : 'directory',
            icon: isLastPart && !file.isDirectory ? getFileIcon(part) : '📁',
            children: [],
            isExpanded: false
          };
          currentNode.children.push(node);
          
          // Sort children after adding new node
          currentNode.children.sort((a, b) => {
            // Directories come before files
            if (a.type !== b.type) {
              return a.type === 'directory' ? -1 : 1;
            }
            // Alphabetical sort within same type
            return a.name.localeCompare(b.name);
          });
        }
        currentNode = node;
      }
    });

    return root;
  }

  function getFileIcon(filename) {
    const ext = filename.split('.').pop().toLowerCase();
    switch (ext) {
      case 'md':
        return '📝';
      case 'todo':
        return '✓';
      default:
        return '📄';
    }
  }

  async function loadFiles() {
    try {
      loading = true;
      error = null;
      const response = await fetch('/api/files', {
        headers: {
          'X-Client': 'web'
        }
      });
      console.log('File list response status:', response.status);
      
      if (response.ok) {
        const data = await response.json();
        console.log('Raw server response:', data);
        
        // Filter out .versions directories and their contents before building the tree
        if (data && Array.isArray(data.files)) {
          files = data.files.filter(file => {
            const path = (file.path || file.Path).replace(/\\/g, '/');
            return !path.includes('.versions/') && !path.includes('.versions\\');
          });
          const tree = buildFileTree(files);
          rootItems = tree.children;
          console.log('Built file tree:', rootItems);
        } else {
          console.error('Invalid response format:', data);
          error = 'Invalid server response format';
        }
      } else {
        console.error('Server error:', response.status, response.statusText);
        error = `Server error: ${response.status} ${response.statusText}`;
      }
    } catch (err) {
      console.error('Error loading files:', err);
      error = err.message;
    } finally {
      loading = false;
    }
  }

  function toggleExpand(node) {
    node.isExpanded = !node.isExpanded;
  }

  async function handleNewFolder() {
    const folderName = prompt('Enter folder name:');
    if (!folderName) return;

    try {
      const response = await fetch('/api/directories', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          path: selectedItem?.type === 'directory' 
            ? `${selectedItem.path}/${folderName}`
            : folderName
        })
      });

      if (response.ok) {
        await loadFiles();
      } else {
        error = 'Failed to create folder';
      }
    } catch (err) {
      console.error('Error creating folder:', err);
      error = err.message;
    }
  }

  async function handleNewNote() {
    const noteName = prompt('Enter note name (without extension):');
    if (!noteName) return;

    const fileName = `${noteName}.md`;
    const path = selectedItem?.type === 'directory' 
      ? `${selectedItem.path}/${fileName}`
      : fileName;

    try {
      const response = await fetch(`/api/files/${path}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'text/plain'
        },
        body: `# ${noteName}\n\n`
      });

      if (response.ok) {
        await loadFiles();
      } else {
        error = 'Failed to create note';
      }
    } catch (err) {
      console.error('Error creating note:', err);
      error = err.message;
    }
  }

  async function handleDelete(item) {
    if (!confirm(`Are you sure you want to delete ${item.name}?`)) return;

    try {
      const response = await fetch(`/api/files/${item.path}`, {
        method: 'DELETE'
      });

      if (response.ok) {
        selectedItem = null;
        await loadFiles();
      } else {
        error = 'Failed to delete item';
      }
    } catch (err) {
      console.error('Error deleting item:', err);
      error = err.message;
    }
  }

  function handleItemClick(node) {
    selectedItem = node;
  }

  onMount(() => {
    if (!item) {
      loadFiles();
    }
  });
</script>

{#if !item}
  <div class="library-container">
    <FileToolbar 
      {selectedItem} 
      on:newFolder={handleNewFolder}
      on:newNote={handleNewNote}
      on:delete={e => handleDelete(e.detail)}
    />
    {#if loading}
      <div class="loading-state">
        <p>Loading your library...</p>
      </div>
    {:else if error}
      <div class="error-state">
        <p>Error loading library: {error}</p>
        <button on:click={loadFiles}>Try Again</button>
      </div>
    {:else}
      <div class="tree-view">
        {#each rootItems as child}
          <svelte:self item={child} level={0} />
        {/each}
      </div>
    {/if}
  </div>
{:else}
  <div class="tree-item" style="margin-left: {level * 20}px">
    <div class="item-content" 
         on:click={() => handleItemClick(item)}
         class:selected={selectedItem === item}>
      {#if item.type === 'directory'}
        <span class="expander">{item.isExpanded ? '▼' : '▶'}</span>
      {:else}
        <span class="expander-placeholder"></span>
      {/if}
      <span class="icon">{item.icon}</span>
      <span class="name">{item.name}</span>
    </div>
    {#if item.isExpanded && item.children && item.children.length > 0}
      <div class="children">
        {#each item.children as child}
          <svelte:self item={child} level={level + 1} />
        {/each}
      </div>
    {/if}
  </div>
{/if}

<style>
  .library-container {
    height: 100%;
    background: var(--background-color, white);
    border-right: 1px solid var(--border-color, #eee);
  }

  .tree-view {
    padding: 1rem;
  }

  .tree-item {
    font-size: 0.9rem;
    line-height: 1.8;
  }

  .item-content {
    display: flex;
    align-items: center;
    padding: 0.2rem 0;
    cursor: pointer;
    user-select: none;
  }

  .item-content:hover {
    background-color: var(--hover-color, #f5f5f5);
  }

  .expander, .expander-placeholder {
    width: 20px;
    text-align: center;
    font-size: 0.8rem;
    color: var(--text-color, #666);
  }

  .icon {
    margin-right: 0.5rem;
  }

  .name {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .children {
    margin-left: 20px;
  }

  .loading-state, .error-state {
    padding: 1rem;
    text-align: center;
    color: var(--text-color, #666);
  }

  .error-state button {
    margin-top: 0.5rem;
    padding: 0.3rem 1rem;
    background-color: var(--primary-color, #007bff);
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
  }

  .error-state button:hover {
    background-color: var(--primary-hover-color, #0056b3);
  }

  .item-content.selected {
    background-color: var(--selection-color, #e3f2fd);
  }
</style> 