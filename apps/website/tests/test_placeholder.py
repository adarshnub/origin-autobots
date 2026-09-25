from html.parser import HTMLParser
from pathlib import Path


class BlankPageParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.in_root = False
        self.root_text: list[str] = []
        self.titles: list[str] = []
        self._in_title = False

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        attrs_dict = dict(attrs)
        if tag == "div" and attrs_dict.get("id") == "root":
            self.in_root = True
        if tag == "title":
            self._in_title = True

    def handle_endtag(self, tag: str) -> None:
        if tag == "div" and self.in_root:
            self.in_root = False
        if tag == "title":
            self._in_title = False

    def handle_data(self, data: str) -> None:
        if self.in_root and data.strip():
            self.root_text.append(data.strip())
        if self._in_title and data.strip():
            self.titles.append(data.strip())


def test_root_is_reserved_as_a_blank_page_with_product_title() -> None:
    source = Path(__file__).resolve().parents[1] / "index.html"
    parser = BlankPageParser()
    parser.feed(source.read_text(encoding="utf-8"))

    assert parser.titles == ["Autobots by Origin Studios"]
    assert parser.root_text == []
