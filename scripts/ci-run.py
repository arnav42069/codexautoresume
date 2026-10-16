#!/usr/bin/env python3
"""Keep normal CI output and expose a short failure tail in public annotations."""
import collections,subprocess,sys
lines=collections.deque(maxlen=150)
process=subprocess.Popen(sys.argv[1:],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,errors='replace')
for line in process.stdout:
    print(line,end='',flush=True);lines.append(line)
code=process.wait()
if code:
    tail=''.join(lines)[-3500:].replace('%','%25').replace('\r','%0D').replace('\n','%0A')
    print('::error title=Platform check failed::'+tail,flush=True)
sys.exit(code)
