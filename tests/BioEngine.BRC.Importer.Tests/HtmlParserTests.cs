using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BioEngine.BRC.Domain.Entities.Blocks;
using BioEngine.Core.Entities.Blocks;
using Xunit;
using Xunit.Abstractions;

namespace BioEngine.BRC.Importer.Tests
{
    public class HtmlParserTest : ImporterTest
    {
        public HtmlParserTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        [Fact]
        public async Task TestParseAsync()
        {
            var html =
                "<p style=\"text-align:center\"><img alt=\"\" src=\"https://cdn.bioware.ru/images/anthem/anthem_logo.jpg\" style=\"height:180px; width:600px\" /></p><p>В прошлый раз генеральный менеджер Bioware <a href=\"https://www.bioware.ru/2019/01/22/anthem_novyj_post_ot_kejsi_hadsona.html\">обращался</a> к сообществу игроков перед выходом демо игры. С тех пор прошло уже почти два месяца и Кейси Хадсон подводит некие итоги этого сложного периода:</p><blockquote><p>Как дела, Фрилансеры? Последние несколько недель для нас были сумасшедшими. С одной стороны, релиз дался нам тяжелее, чем планировалось. Но с другой, мы знали, что большие новые онлайн-игры, как правило, сталкиваются с разного рода проблемами на релизе, так что как бы мы ни тестировали и готовились, мы также осознавали, что можем столкнуться с непредвиденными трудностями. И мы по-прежнему намерены прилагать все усилия к тому, чтобы справляться с ними.</p><p>От многих из вас мы слышим, что у выпущенной нами игры очень увлекательный основной геймплей, но вместе с тем обнаружился ряд проблем, которые не проявлялись, пока мы не начали работать в масштабах миллионов игроков. Конечно же, мы были разочарованы этим не меньше вас. Я тоже играл с самого начала (за Рейнджера в цветах команды Edmonton Oilers!) вместе с вами, и меня, разумеется, расстраивают любые вещи, которые мешают кому-то полностью насладиться игрой. Я принимаю это близко к сердцу, и нашей первоочередной задачей было улучшить ситуацию самым быстрым и безопасным способом.</p><p>Над этим работала наша команда лайв-сервиса, которая в течение первых нескольких недель уже добавила с помощью патчей и обновлений более 200 отдельных изменений, направленных на улучшение стабильности, лута и прогресса, кастомизации и многого другого.</p><p>Мы также продолжаем прислушиваться к вашим отзывам, в скором времени вас ждут новые обновления, связанные с лутом и прокачкой в эндгейме, геймплеем, стабильностью и производительностью &ndash; нам предстоит еще много работы. Для нас это все очень важный&nbsp;опыт, и пока мы работаем над улучшением и усовершенствованием игры, стоит особо подчеркнуть, насколько мы ценим то, что вы остаетесь с нами. Тем более, что как раз на следующем этапе события станут намного более захватывающими.</p><p style=\"text-align:center\"><img alt=\"\" src=\"https://cdn.bioware.ru/images/anthem/news/march_post.jpg\" style=\"height:394px; width:700px\" /></p><br />По мере того, как мы проходим через самый трудный период запуска новой игры и IP, команды также работают над тем, что действительно покажет, на что способен Anthem &ndash; серией мировых событий, новым сюжетным контентом и новыми функциями, которые ведут к <a href=\"https://www.ea.com/games/anthem/acts\">Катаклизму</a> позже этой весной.<p>Мы понимаем, что некоторые настроены скептически. Мы слышим критику и сомнения. Но все равно будем продолжать, каждый день усердно работая над Anthem &ndash; постоянно меняющимся, улучшающимся и растущим миром, который и дальше будет поддерживать наша команда увлеченных разработчиков.</p><p>С Anthem мы пробуем что-то немного отличающееся от&nbsp;наших предыдущих проектов. И так же наши следующие игры будут отличаться от Anthem. Но во всем, что мы делаем, мы стремимся оставаться верными нашей миссии, создавая миры, которые вдохновляют вас стать героем своей собственной истории. Поэтому для нас важнее всего вы &ndash; игроки, которые поддержали нас в этом путешествии. И мы рады будем доказать, что для Anthem лучшее еще впереди.</p><p>&ndash; Кейси.</p></blockquote><p>&nbsp;</p><p>&nbsp;</p><p>&nbsp;</p>";

            var scope = GetScope();
            var parser = scope.Get<HtmlParser>();
            var blocks = await parser.ParseAsync(html, "/", new List<GalleryExport>());
            Assert.NotEmpty(blocks);
            Assert.True(blocks.Count == 3);
            Assert.IsType<PictureBlock>(blocks.First());
            Assert.IsType<TextBlock>(blocks[1]);
            Assert.IsType<QuoteBlock>(blocks.Last());
        }

        [Fact]
        public async Task TestParseIframeAsync()
        {
            var html = "<div><iframe src=\"https://www.bioware.ru\" /></div>";
            var scope = GetScope();
            var parser = scope.Get<HtmlParser>();
            var blocks = await parser.ParseAsync(html, "/", new List<GalleryExport>());
            Assert.NotEmpty(blocks);
            Assert.True(blocks.Count == 1);
            Assert.IsType<IframeBlock>(blocks.First());
            if (blocks.First() is IframeBlock iframeBlock)
            {
                Assert.Equal("https://www.bioware.ru", iframeBlock.Data.Src);
            }
        }

        [Fact]
        public async Task TestParseYoutubeAsync()
        {
            var html =
                @"<p style=""text-align:center""><iframe width=""600"" height=""338"" src=""//www.youtube.com/embed/8bHwTDl231A?rel=0"" frameborder=""0""></iframe></p>";
            var scope = GetScope();
            var parser = scope.Get<HtmlParser>();
            var blocks = await parser.ParseAsync(html, "/", new List<GalleryExport>());
            Assert.NotEmpty(blocks);
            Assert.True(blocks.Count == 1);
            Assert.IsType<YoutubeBlock>(blocks.First());
            if (blocks.First() is YoutubeBlock youtubeBlock)
            {
                Assert.Equal("8bHwTDl231A", youtubeBlock.Data.YoutubeId);
            }
        }

        [Fact]
        public async Task TestParseTwitchAsync()
        {
            var html = "<div><iframe src=\"https://player.twitch.tv/?channel=quin69\" /></div>";
            var scope = GetScope();
            var parser = scope.Get<HtmlParser>();
            var blocks = await parser.ParseAsync(html, "/", new List<GalleryExport>());
            Assert.NotEmpty(blocks);
            Assert.True(blocks.Count == 1);
            Assert.IsType<TwitchBlock>(blocks.First());
            if (blocks.First() is TwitchBlock twitchBlock)
            {
                Assert.Equal("quin69", twitchBlock.Data.ChannelId);
            }
        }

        [Fact]
        public async Task TestParsePictureAsync()
        {
            var html =
                "<p style=\"text-align:center\"><img alt=\"\" src=\"https://cdn.bioware.ru/images/anthem/anthem_logo.jpg\" style=\"height:180px; width:600px\" /></p>";
            var scope = GetScope();
            var parser = scope.Get<HtmlParser>();
            var blocks = await parser.ParseAsync(html, "/", new List<GalleryExport>());
            Assert.NotEmpty(blocks);
            Assert.True(blocks.Count == 1);
            Assert.IsType<PictureBlock>(blocks.First());
            Assert.True(blocks.First() is PictureBlock pictureBlock && pictureBlock.Data.Picture.FileSize > 0);
        }

        [Fact]
        public async Task TestParsePlainTextAsync()
        {
            var html =
                "Эх, не выдержал Фергус Уркхарт (глава Obsidian Entertainment) и написал пост в теме обсуждения слухов о неанонсированных проектах своей студии на форумах RPG Codex. <br /><br />      Как и предполагалось, обсидиановцы уже вовсю работают над одним неанонсированным проектом и собираются приступить к разработке другого в ближайшие недели. Фергус также сокрушался по поводу низких продаж PC-игр и с сожалением отметил, что для того, чтобы остаться в игровом бизнесе компании придётся заняться разработкой игр для консолей. Главной своей целью на ближайшее время господин Уркхарт считает создание в Obsidian Entertainment трёх полноценных команд, каждая из которых будет заниматься собственным проектом. Расклад следующий: две команды будут разрабатывать игры для консолей и лишь одна займётся PC-проектами.    <p align=\"right\">Новость с <a href=\"http://www.crpg.ru\">CRPG.RU</a></p>";

            var scope = GetScope();
            var parser = scope.Get<HtmlParser>();
            var blocks = await parser.ParseAsync(html, "/", new List<GalleryExport>());
            Assert.NotEmpty(blocks);
            Assert.Equal(2, blocks.Count);
            Assert.IsType<TextBlock>(blocks.First());
            Assert.IsType<TextBlock>(blocks.Last());
        }

        [Fact]
        public async Task TestParsePlainTextWithLinksAsync()
        {
            var html =
                "Закончился приём последних работ в литературном конкурсе BioWare (в котором, напоминаем, требовалось создать небольшой авторский модуль при помощи тулсета Neverwinter Nights). Просмотреть все полученные работы и проголосовать за них Вы сможете <a href=\"http://nwvault.ign.com/View.php?view=Modules.List&amp;category_select_id=28\">здесь</a>. Голосование продлится до 28 февраля, после чего будут оглашены победители, некоторые из которых станут полноправными сотрудниками BioWare. Также, всех победителей ждут и менее значимые призы, вроде кепок и футболок с логотипами BioWare.";

            var scope = GetScope();
            var parser = scope.Get<HtmlParser>();
            var blocks = await parser.ParseAsync(html, "/", new List<GalleryExport>());
            Assert.NotEmpty(blocks);
            Assert.Equal(1, blocks.Count);
        }
    }
}
