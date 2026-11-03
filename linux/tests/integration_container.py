"""Opt-in integration test: ONLY inside a disposable test container.

Requires installed helper, systemd-resolved, dbus and root inside that container.
It creates a localhost-only DNS server and temporarily changes container /etc/hosts.
Never run on your desktop. Invocation: python3 integration_container.py --isolated-container
"""
import json,os,pwd,select,socket,struct,subprocess,sys,threading,time
from pathlib import Path
assert Path('/.dockerenv').exists() and '--isolated-container' in sys.argv, 'Requires explicit opt-in inside a disposable container'
server=socket.socket(socket.AF_INET,socket.SOCK_DGRAM); server.bind(('127.0.0.1',15353)); server.settimeout(.2)
stop=threading.Event()
def dns():
 while not stop.is_set():
  try: packet,peer=server.recvfrom(4096)
  except socket.timeout: continue
  offset=12
  while packet[offset]: offset+=packet[offset]+1
  end=offset+5; qtype=struct.unpack('!H',packet[offset+1:offset+3])[0]
  answer=b'\xc0\x0c'+struct.pack('!HHIH',1,1,300,4)+socket.inet_aton('192.0.2.55') if qtype==1 else b''
  reply=packet[:2]+struct.pack('!HHHHH',0x8180,1,bool(answer),0,0)+packet[12:end]+answer
  server.sendto(reply,peer)
threading.Thread(target=dns,daemon=True).start()
Path('/run/dbus').mkdir(exist_ok=True)
conf=Path('/etc/systemd/resolved.conf.d/pickads-test.conf'); conf.parent.mkdir(exist_ok=True)
conf.write_text('[Resolve]\nDNS=127.0.0.1:15353\nFallbackDNS=\nDNSStubListener=no\nLLMNR=no\nMulticastDNS=no\nDNSSEC=no\nDNSOverTLS=no\nCache=yes\nCacheFromLocalhost=yes\n')
logs=open('/tmp/pickads-services.log','w')
bus=subprocess.Popen(['/usr/bin/dbus-daemon','--system','--nofork','--nopidfile'],stdout=logs,stderr=logs)
resolved=None; helper=None; original=Path('/etc/hosts').read_bytes()
try:
 for _ in range(30):
  if Path('/run/dbus/system_bus_socket').exists(): break
  time.sleep(.1)
 identity=pwd.getpwnam('systemd-resolve')
 runtime=Path('/run/systemd/resolve'); runtime.mkdir(parents=True,exist_ok=True); os.chown(runtime,identity.pw_uid,identity.pw_gid)
 resolved=subprocess.Popen(['/usr/lib/systemd/systemd-resolved'],stdout=logs,stderr=logs)
 for _ in range(50):
  if Path('/run/systemd/resolve/io.systemd.Resolve.Monitor').exists(): break
  if resolved.poll() is not None: raise RuntimeError('resolved failed; inspect services log')
  time.sleep(.1)
 for _ in range(50):
  query=subprocess.run(['/usr/bin/resolvectl','query','--type=A','ads.integration.test'],capture_output=True,timeout=10)
  if query.returncode==0: break
  time.sleep(.1)
 else: raise RuntimeError(query.stderr.decode())
 raw=subprocess.run(['/usr/bin/resolvectl','--json=short','show-cache'],check=True,capture_output=True).stdout
 Path('/tmp/pickads-real-cache.json').write_bytes(raw)
 helper=subprocess.Popen(['/usr/libexec/pickads-helper'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
 def request(obj):
  helper.stdin.write(json.dumps(obj)+'\n'); helper.stdin.flush()
  if not select.select([helper.stdout],[],[],20)[0]: raise RuntimeError('helper did not respond')
  reply=json.loads(helper.stdout.readline()); assert reply.get('ok'), reply
  return reply
 scan=request({'action':'scan'}); assert 'ads.integration.test' in scan['domains'],scan
 print('PASS: real systemd-resolved cache scan detected the local DNS fixture')
 request({'action':'apply','enabled':True,'domains':['ads.integration.test']})
 addresses={item[4][0] for item in socket.getaddrinfo('ads.integration.test',None)}
 assert addresses=={'0.0.0.0','::'},addresses
 print('PASS: installed helper blocked IPv4 and IPv6 lookups through the container hosts file')
 request({'action':'flush'})
 assert 'ads.integration.test' not in request({'action':'scan'})['domains']
 print('PASS: real DNS cache clear')
 request({'action':'apply','enabled':False,'domains':[]})
 assert Path('/etc/hosts').read_bytes()==original
 print('PASS: disable restored every original hosts byte')
 helper.stdin.close(); assert helper.wait(timeout=5)==0
 print('PASS: root helper exits on session EOF')
finally:
 Path('/etc/hosts').write_bytes(original)
 if helper is not None and helper.poll() is None: helper.kill(); helper.wait()
 if resolved is not None: resolved.terminate(); resolved.wait(timeout=5)
 bus.terminate(); bus.wait(timeout=5)
 stop.set(); server.close(); conf.unlink(missing_ok=True)
