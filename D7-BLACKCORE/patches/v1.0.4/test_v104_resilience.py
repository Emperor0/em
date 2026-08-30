import json
from unittest.mock import patch
from blackcore.planner import Planner
from blackcore.swarm import SwarmEngine
from blackcore.llm import LLMProvider


def test_research_report_uses_bounded_fast_path():
    goal=('افتح المتصفح، ابحث في الإنترنت عن أحدث إصدار مستقر من Python، تحقق من المعلومة من مصدر موثوق، '
          'ثم أنشئ على سطح المكتب ملف باسم PYTHON_RESEARCH.txt يحتوي على الإصدار والمصدر وتاريخ التحقق. '
          'بعد ذلك اقرأ الملف وتأكد أن المحتوى تم حفظه بشكل صحيح وقدّم دليل التنفيذ.')
    m=Planner().plan(goal)
    actions=[s.action for s in m.steps]
    assert actions == ['search_web','research_analysis','write_context_text','system:read_text','quality_review']
    assert m.steps[0].args['query'] == 'أحدث إصدار مستقر من Python'
    assert m.steps[2].args['path'].endswith(r'Desktop\PYTHON_RESEARCH.txt')


class PartialLLM:
    def available(self): return True
    def chat(self, system, user, timeout=60, **kwargs):
        if 'Fact Checker' in system: raise TimeoutError('simulated timeout')
        if 'Master Judge' in system: return 'synthesis'
        return 'specialist output'


def test_swarm_preserves_partial_success():
    r=SwarmEngine(PartialLLM(),max_workers=3).run_council('goal',['Researcher','Fact Checker','Analyst'])
    assert r.ok
    assert r.data['text']=='synthesis'
    assert len(r.data['outputs'])==2
    assert 'Fact Checker' in r.data['errors']


class FakeResponse:
    def __init__(self,obj): self.obj=obj
    def __enter__(self): return self
    def __exit__(self,*a): pass
    def read(self): return json.dumps(self.obj).encode()


def test_opencode_abort_active_session():
    calls=[]
    def fake(req, timeout=0):
        calls.append((req.full_url,req.method))
        if req.full_url.endswith('/session/ses_x/abort'):
            return FakeResponse({'ok':True})
        raise AssertionError(req.full_url)
    with patch('urllib.request.urlopen',fake):
        p=LLMProvider('http://127.0.0.1:8082',auto_start=False)
        p._detected_type='opencode'; p._set_active_session('ses_x')
        assert p.abort_active()
        assert calls[0][0].endswith('/session/ses_x/abort')
