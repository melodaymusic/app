const periods = [
  {name:'Dawn', time:'05:00', title:'Ease into the day.', description:'A mix for the first part of the day, drawn from your own collection.', sky:'#2d2350',sun:'#ff6e4b',top:'90%'},
  {name:'Morning', time:'09:00', title:'A brighter start.', description:'A mix for the morning, picked from the music you already have.', sky:'#3ca5e1',sun:'#ffc800',top:'50%'},
  {name:'Afternoon', time:'14:00', title:'Keep the afternoon going.', description:'As the day moves on, Meloday finds another mix in your library.', sky:'#1e5adc',sun:'#ffc800',top:'12%'},
  {name:'Evening', time:'19:00', title:'Settle into the evening.', description:'The cover changes with the hour. The music comes from your listening.', sky:'#873769',sun:'#ff6e4b',top:'90%'},
  {name:'Late night', time:'01:00', title:'Stay a little longer.', description:'A late-night mix for the last few songs of the day.', sky:'#0a3228',sun:'#739bf5',top:'25%'}
];
document.querySelectorAll('[data-day]').forEach(button => button.addEventListener('click', () => {
  const period = periods[Number(button.dataset.day)];
  document.querySelectorAll('[data-day]').forEach(item => item.setAttribute('aria-pressed',String(item === button)));
  document.querySelector('#day-kicker').textContent = `${period.time} / ${period.name.toUpperCase()}`;
  document.querySelector('#day-title').textContent = period.title;
  document.querySelector('#day-description').textContent = period.description;
  document.querySelector('#art-period').textContent = period.name;
  document.querySelector('#day-art').style.background = period.sky;
  document.querySelectorAll('.sun-rings i').forEach(ring => {ring.style.background = period.sun;ring.style.top = period.top;});
}));
const dialog = document.querySelector('#video-dialog');
const video = dialog.querySelector('video');
document.querySelector('#watch').addEventListener('click',()=>dialog.showModal());
document.querySelector('#close-video').addEventListener('click',()=>dialog.close());
dialog.addEventListener('close',()=>video.pause());
dialog.addEventListener('click',event=>{if(event.target===dialog){const bounds=dialog.getBoundingClientRect();if(event.clientX<bounds.left||event.clientX>bounds.right||event.clientY<bounds.top||event.clientY>bounds.bottom)dialog.close();}});
