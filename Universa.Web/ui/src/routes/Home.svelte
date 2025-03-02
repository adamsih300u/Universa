<script>
  let stats = {
    totalFiles: 0,
    lastSync: null,
    syncStatus: 'idle'
  };

  async function loadStats() {
    try {
      const response = await fetch('/api/stats');
      if (response.ok) {
        stats = await response.json();
      }
    } catch (error) {
      console.error('Error loading stats:', error);
    }
  }

  async function triggerSync() {
    try {
      stats.syncStatus = 'syncing';
      const response = await fetch('/api/sync', { method: 'POST' });
      if (response.ok) {
        await loadStats();
        alert('Sync completed successfully!');
      }
    } catch (error) {
      console.error('Error during sync:', error);
      alert('Sync failed');
    } finally {
      stats.syncStatus = 'idle';
    }
  }
</script>

<div class="home">
  <h2>Welcome to Universa</h2>
  
  <div class="stats">
    <div class="stat-card">
      <h3>Total Files</h3>
      <p>{stats.totalFiles}</p>
    </div>

    <div class="stat-card">
      <h3>Last Sync</h3>
      <p>{stats.lastSync ? new Date(stats.lastSync).toLocaleString() : 'Never'}</p>
    </div>
  </div>

  <button 
    on:click={triggerSync} 
    disabled={stats.syncStatus === 'syncing'}
  >
    {stats.syncStatus === 'syncing' ? 'Syncing...' : 'Sync Now'}
  </button>
</div>

<style>
  .home {
    padding: 1rem;
  }

  h2 {
    color: #2c3e50;
    margin-bottom: 2rem;
    text-align: center;
  }

  .stats {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
    gap: 1rem;
    margin-bottom: 2rem;
  }

  .stat-card {
    background-color: #f8f9fa;
    border-radius: 8px;
    padding: 1rem;
    text-align: center;
  }

  .stat-card h3 {
    color: #6c757d;
    margin-bottom: 0.5rem;
  }

  .stat-card p {
    font-size: 1.5rem;
    color: #2c3e50;
    margin: 0;
  }

  button {
    display: block;
    margin: 0 auto;
    background-color: #28a745;
    color: white;
    border: none;
    padding: 0.75rem 1.5rem;
    border-radius: 4px;
    cursor: pointer;
  }

  button:hover:not(:disabled) {
    background-color: #218838;
  }

  button:disabled {
    background-color: #6c757d;
    cursor: not-allowed;
  }
</style> 