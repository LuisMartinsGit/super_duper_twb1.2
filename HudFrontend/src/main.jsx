import React from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';
import './bridge.js'; // installs window.unityHUD
import { HudFrame } from './components/HudFrame.jsx';

// Mark <body> so the styles flip to transparent in-game mode. To preview the
// design with the fake game-area vignette/grain, remove the class from the
// host page (or set ?preview=1 in the URL).
const preview = new URLSearchParams(location.search).get('preview') === '1';
if (!preview) document.body.classList.add('in-game');

const root = createRoot(document.getElementById('root'));
root.render(<HudFrame themeKey="jade" accentKey="theme" ornament="maximal" />);
