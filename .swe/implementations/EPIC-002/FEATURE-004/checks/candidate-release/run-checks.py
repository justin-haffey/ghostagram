from pathlib import Path
import json,subprocess,time,sys
out=Path(__file__).resolve().parent
if len(sys.argv)>1:out=out/sys.argv[1]
out.mkdir(exist_ok=True)
suites=['Ghostagram.Core.Tests','Ghostagram.Bridge.Tests','Ghostagram.Execution.Tests','Ghostagram.Cutover.Tests','Ghostagram.Persistence.Tests','Ghostagram.Server.GraphWorkspace.Tests','Ghostagram.Layout.Verification']
cases=[(x, [f'tests/{x}/bin/Release/net10.0/{x}.dll']) for x in suites]
cases += [('composition-foundation',['tests/Ghostagram.Bridge.Tests/bin/Release/net10.0/Ghostagram.Bridge.Tests.dll','--profile','composition']),('benchmark',['benchmarks/Ghostagram.Bridge.Benchmarks/bin/Release/net10.0/Ghostagram.Bridge.Benchmarks.dll','--repetitions','1','--sizes','100,500,1000'])]
fixture='../ghostworx-system/tests/Ghostworx.System.Variable.Conformance/Fixtures/variable/v1'
graph='../ghostworx-system/tests/Ghostworx.System.Graph.Conformance/Fixtures/semantic-graph/v1'
schema='../ghostworx-system/tests/Ghostworx.System.Graph.Conformance/Schemas/conformance-result.schema.json'
links='../ghostworx-system/tests/Ghostworx.System.SemanticLinks.Conformance/Corpus/V1'
profile='../ghostworx-system/tests/Ghostworx.System.SemanticLinks.Conformance/Profiles/semantic-link-conformance-v1.json'
test='tests/Ghostagram.Graph.Conformance.Tests/bin/Release/net10.0/Ghostagram.Graph.Conformance.Tests.dll'
runner='tests/Ghostagram.Graph.Conformance/bin/Release/net10.0/Ghostagram.Graph.Conformance.dll'
fingerprint=(Path(__file__).resolve().parent/'source-fingerprint.txt').read_text().strip()
version='d3f2542c28acfed7ecf04f9c37a4e47241138f29+f004.'+fingerprint[:16]
cases += [('graph-conformance-tests',[test]),('variable-conformance-tests',[test,'--feature','feature-002','--fixtures',fixture]),('links-conformance-tests',[test,'--feature','feature-003','--corpus',links,'--profile',profile]),
 ('graph-envelope',[runner,'--corpus',graph,'--schema',schema,'--output',str(out/'graph-result.json'),'--participant-version',version]),
 ('variable-envelope',[runner,'--feature','feature-002','--fixtures',fixture,'--result',str(out/'variable-result.json'),'--participant-version',version]),
 ('links-envelope',[runner,'--feature','feature-003','--corpus',links,'--profile',profile,'--result',str(out/'links-result.json'),'--participant-version',version])]
if len(sys.argv)>2:cases=[case for case in cases if case[0] in sys.argv[2:]]
results=[]
for name,args in cases:
 start=time.monotonic();fatal=None;forced=None
 stdout=out/(name+'.stdout.log');stderr=out/(name+'.stderr.log')
 with stdout.open('wb') as so,stderr.open('wb') as se:
  proc=subprocess.Popen(['dotnet']+args,stdout=so,stderr=se,creationflags=subprocess.CREATE_NO_WINDOW)
  while proc.poll() is None:
   elapsed=time.monotonic()-start
   if fatal is None and b'Unhandled exception' in stderr.read_bytes():fatal=time.monotonic()
   if elapsed>60 or (fatal is not None and time.monotonic()-fatal>5):
    forced='owned-process-timeout' if elapsed>60 else 'owned-process-unhandled-exception-hang';proc.kill();break
   time.sleep(.1)
  native=proc.wait(timeout=10)
 record={'name':name,'command':['dotnet']+args,'exitCode':None if forced else native,'ownedProcessExitCode':native,'forcedTermination':forced,'seconds':round(time.monotonic()-start,3),'stdout':stdout.name,'stderr':stderr.name}
 results.append(record);(out/'ordinary-results.json').write_text(json.dumps(results,indent=2)+'\n')
 print(name+': '+('PASS' if native==0 and not forced else 'FAIL')+' '+str(record['seconds'])+'s',flush=True)
 if native!=0 or forced:print(stderr.read_text(errors='replace')[-2500:],flush=True)
