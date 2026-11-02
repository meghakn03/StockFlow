window.sf = {
 token:null,
 async request(method,path,data) {
  if(method!=='GET' && !this.token){const r=await fetch('/api/auth/csrf');if(!r.ok)throw new Error('Your session expired. Sign in again.');this.token=(await r.json()).token;}
  const r=await fetch('/api'+path,{method,headers:{'Content-Type':'application/json',...(this.token?{'X-CSRF-TOKEN':this.token}:{})},...(data===null||data===undefined?{}:{body:JSON.stringify(data)})});
  if(!r.ok){let e;try{e=await r.json();}catch{}throw new Error(e?.detail||(r.status===401?'Your session expired. Sign in again.':r.status===403?'This action requires an administrator.':'Request failed. Please try again.'));}
  return r.status===204?null:await r.json();
 },
 logout:async function(){await this.request('POST','/auth/logout');location.href='/login';}
};
