﻿using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SSR.Net.Services;

namespace SSR.Net.DotNet6.Controllers
{
    public class HomeController : Controller
    {
        private readonly React17Renderer _react17Renderer;
        private readonly React18Renderer _react18Renderer;
        private readonly Vue3Renderer _vue3Renderer;

        public HomeController(React17Renderer react17Renderer,
                              React18Renderer react18Renderer,
                              Vue3Renderer vue3Renderer)
        {
            _react17Renderer = react17Renderer;
            _react18Renderer = react18Renderer;
            _vue3Renderer = vue3Renderer;
        }

        public ActionResult Index() => View();

        public ActionResult React17() =>
            View(_react17Renderer.RenderComponent(
            "Components.FrontPage",
            JsonConvert.SerializeObject(
                new
                {
                    header = "React 17 with SSR",
                    links = new[]{
                    new {
                        text = "Google.com",
                        href ="https://www.google.com"
                    },
                    new {
                        text = "Hacker news",
                        href = "https://news.ycombinator.org"
                    }
                }
                })));

        public ActionResult React18() =>
            View(_react18Renderer.RenderComponent(
                "Components.FrontPage",
                JsonConvert.SerializeObject(
                    new
                    {
                        header = "React 18 with SSR",
                        links = new[]{
                        new {
                            text = "Google.com",
                            href = "https://www.google.com"
                        },
                        new {
                            text = "Hacker news",
                            href = "https://news.ycombinator.org"
                        }
                    }
                    })));

        public ActionResult Vue3() =>
            View(_vue3Renderer.RenderComponent(
                "Components.Example",
                JsonConvert.SerializeObject(
                    new
                    {
                        title = "Vue 3 with SSR"
                    }
                )));
    }
}