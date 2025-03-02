<script>
  import { onMount, onDestroy } from 'svelte';

  let dateTime = '';
  let weather = null;
  let moonPhase = null;
  let weatherEnabled = false;
  let moonPhaseEnabled = false;
  let apiKey = '';
  let location = '';
  let interval;

  onMount(async () => {
    // Load settings from localStorage
    const settings = JSON.parse(localStorage.getItem('statusBarSettings') || '{}');
    weatherEnabled = settings.weatherEnabled || false;
    moonPhaseEnabled = settings.moonPhaseEnabled || false;
    apiKey = settings.apiKey || '';
    location = settings.location || '';

    // Start updating date and time
    updateDateTime();
    interval = setInterval(updateDateTime, 1000);

    // If weather is enabled and we have the necessary settings, fetch weather
    if (weatherEnabled && apiKey && location) {
      await updateWeather();
      // Update weather every 30 minutes
      setInterval(updateWeather, 30 * 60 * 1000);
    }
  });

  onDestroy(() => {
    if (interval) clearInterval(interval);
  });

  function updateDateTime() {
    const now = new Date();
    const options = {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      hour12: true
    };
    
    // Get the formatted date parts
    const weekday = now.toLocaleDateString('en-US', { weekday: 'long' });
    const month = now.toLocaleDateString('en-US', { month: 'long' });
    const day = now.getDate();
    const year = now.getFullYear();
    const time = now.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit', hour12: true });

    // Combine with proper ordinal suffix
    dateTime = `${weekday}, ${month} ${day}${getOrdinalSuffix(day)}, ${year} - ${time}`;
  }

  function getOrdinalSuffix(day) {
    if (day > 3 && day < 21) return 'th';
    switch (day % 10) {
      case 1: return 'st';
      case 2: return 'nd';
      case 3: return 'rd';
      default: return 'th';
    }
  }

  async function updateWeather() {
    try {
      const response = await fetch(
        `https://api.openweathermap.org/data/2.5/weather?q=${encodeURIComponent(location)}&appid=${apiKey}&units=metric`
      );
      if (response.ok) {
        const data = await response.json();
        weather = {
          temp: Math.round(data.main.temp),
          description: data.weather[0].description,
          icon: data.weather[0].icon
        };
        
        // Calculate moon phase (simplified)
        const now = new Date();
        const phase = getMoonPhase(now);
        moonPhase = phase;
      }
    } catch (error) {
      console.error('Error fetching weather:', error);
    }
  }

  function getMoonPhase(date) {
    const phase = ((date.getTime() / 1000 - 1592591580) / (29.53 * 24 * 60 * 60)) % 1;
    const phases = ['🌑', '🌒', '🌓', '🌔', '🌕', '🌖', '🌗', '🌘'];
    return phases[Math.floor(phase * 8)];
  }
</script>

<div class="status-bar">
  <div class="left">
    {dateTime}
  </div>
  <div class="right">
    {#if weatherEnabled && weather}
      <span class="weather">
        {weather.temp}°C {weather.description}
        <img 
          src={`http://openweathermap.org/img/w/${weather.icon}.png`}
          alt={weather.description}
          class="weather-icon"
        />
      </span>
    {/if}
    {#if moonPhaseEnabled && moonPhase}
      <span class="moon-phase">{moonPhase}</span>
    {/if}
  </div>
</div>

<style>
  .status-bar {
    height: 24px;
    background-color: var(--section-bg);
    border-top: 1px solid var(--border-color);
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 0 1rem;
    font-size: 0.85rem;
    color: var(--text-color);
  }

  .right {
    display: flex;
    align-items: center;
    gap: 1rem;
  }

  .weather {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  .weather-icon {
    width: 20px;
    height: 20px;
  }

  .moon-phase {
    font-size: 1.1rem;
  }
</style> 