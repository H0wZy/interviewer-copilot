import time,sys,os
t=time.perf_counter()
from faster_whisper import WhisperModel
t1=time.perf_counter()
m=WhisperModel(sys.argv[1],device="cpu",compute_type="int8",cpu_threads=2,download_root=sys.argv[2],local_files_only=True)
t2=time.perf_counter()
import numpy as np
m.transcribe(np.zeros(16000*2,np.float32),language="en",beam_size=1)
list(m.transcribe(np.zeros(16000*2,np.float32),language="en",beam_size=1)[0])
t3=time.perf_counter()
print(sys.argv[1],"import_s",round(t1-t,2),"load_s",round(t2-t1,2),"first_infer_s",round(t3-t2,2))
