// Generates the 14 theme ResourceDictionaries. Run:  node Themes/gen-themes.mjs
// Every theme defines the exact same key set so Styles.xaml can bind everything with DynamicResource.
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));

const base = {
  font: "Segoe UI",
  startText: "",
  logoStyle: "Fluent",
  buttonRadius: "6",
  startRadius: null,          // defaults to buttonRadius
  taskbarRadius: "0",
  menuRadius: "8",
  taskbarBorderThickness: "0,1,0,0",
  buttonBorderThickness: "0",
  buttonBorder: "#00000000",
  indicatorHeight: "3",
  startMinWidth: "48",
  menuForeground: null,       // defaults to fg
  menuItemHoverForeground: null,
  startForeground: null,
  startHover: null,
  popupShadow: "#80000000",
};

const themes = [
  { file: "FluentDark", name: "Fluent Dark",
    bg: "#E6202020", border: "#30FFFFFF", fg: "#FFFFFF", muted: "#A0FFFFFF",
    hover: "#1EFFFFFF", pressed: "#12FFFFFF", active: "#28FFFFFF", indicator: "#4CC2FF",
    startBg: "#00000000", accent: "#4CC2FF", menuBg: "#F2202020", menuBorder: "#30FFFFFF", menuHover: "#1EFFFFFF",
    inputBg: "#2D2D2D", inputBorder: "#50FFFFFF", logo: ["#4CC2FF","#4CC2FF","#4CC2FF","#4CC2FF"], font: "Segoe UI Variable Display, Segoe UI" },

  { file: "FluentLight", name: "Fluent Light",
    bg: "#F2F3F3F3", border: "#20000000", fg: "#1B1B1B", muted: "#80000000",
    hover: "#12000000", pressed: "#08000000", active: "#1A000000", indicator: "#005FB8",
    startBg: "#00000000", accent: "#005FB8", menuBg: "#F8F9F9F9", menuBorder: "#20000000", menuHover: "#12000000",
    inputBg: "#FFFFFF", inputBorder: "#30000000", logo: ["#0078D4","#0078D4","#0078D4","#0078D4"], font: "Segoe UI Variable Display, Segoe UI" },

  { file: "Aero", name: "Aero",
    bgGradient: ["#C04F7FBF","#B0244B86","#C01A3A66"], border: "#90FFFFFF", fg: "#FFFFFF", muted: "#C0FFFFFF",
    hover: "#40FFFFFF", pressed: "#60FFFFFF", active: "#55FFFFFF", indicator: "#00000000",
    startRadial: ["#7FC5FF","#2F7FD8","#0B3F8F"], accent: "#4FA8FF", menuBg: "#E0335C8C", menuBorder: "#90FFFFFF", menuHover: "#40FFFFFF",
    inputBg: "#FFFFFF", inputBorder: "#7FA8D8", logo: ["#F35325","#81BC06","#05A6F0","#FFBA08"],
    logoStyle: "Aero", buttonRadius: "4", startRadius: "24", indicatorHeight: "0", startMinWidth: "56", startHover: "#30FFFFFF",
    menuForeground: "#FFFFFF" },

  { file: "Luna", name: "Luna",
    bgGradient: ["#3C81F3","#245EDC","#1941A5"], border: "#0F3C9F", fg: "#FFFFFF", muted: "#D0DFFF",
    hover: "#3F8CF3", pressed: "#1D4FB3", active: "#1E4FB0", indicator: "#00000000",
    startGradient: ["#3FAE3F","#2E8F2E","#1F6F1F"], startHover: "#4DBA4A", accent: "#F0A716", menuBg: "#FFFFFF", menuBorder: "#0A246A", menuHover: "#316AC5",
    inputBg: "#FFFFFF", inputBorder: "#7F9DB9", logo: ["#E8412D","#46B535","#1C7BD9","#F4C526"],
    logoStyle: "Luna", font: "Tahoma", startText: "start", buttonRadius: "3", startRadius: "0,12,12,0", indicatorHeight: "0",
    startMinWidth: "104", taskbarBorderThickness: "0,2,0,0", menuForeground: "#000000", menuItemHoverForeground: "#FFFFFF" },

  { file: "Classic", name: "Classic",
    bg: "#C0C0C0", border: "#FFFFFF", fg: "#000000", muted: "#404040",
    hover: "#CDCDCD", pressed: "#A8A8A8", active: "#E4E4E4", indicator: "#00000000",
    startBg: "#C0C0C0", startHover: "#CDCDCD", accent: "#000080", menuBg: "#C0C0C0", menuBorder: "#808080", menuHover: "#000080",
    inputBg: "#FFFFFF", inputBorder: "#808080", logo: ["#F65314","#7CBB00","#00A1F1","#FFBB00"],
    logoStyle: "Classic", font: "Microsoft Sans Serif", startText: "Start", buttonRadius: "0", menuRadius: "0", indicatorHeight: "0",
    buttonBorder: "#808080", buttonBorderThickness: "1", startMinWidth: "76", menuItemHoverForeground: "#FFFFFF" },

  { file: "MetroDark", name: "Metro Dark",
    bg: "#FF101010", border: "#FF2B2B2B", fg: "#FFFFFF", muted: "#A0FFFFFF",
    hover: "#33FFFFFF", pressed: "#22FFFFFF", active: "#FF2B2B2B", indicator: "#76B9ED",
    startBg: "#00000000", accent: "#0078D7", menuBg: "#F5101010", menuBorder: "#FF2B2B2B", menuHover: "#33FFFFFF",
    inputBg: "#FF2B2B2B", inputBorder: "#FF3F3F3F", logo: ["#FFFFFF","#FFFFFF","#FFFFFF","#FFFFFF"],
    logoStyle: "Metro", buttonRadius: "0", menuRadius: "0", indicatorHeight: "2" },

  { file: "Dracula", name: "Dracula",
    bg: "#F2282A36", border: "#44475A", fg: "#F8F8F2", muted: "#6272A4",
    hover: "#44475A", pressed: "#6272A4", active: "#44475A", indicator: "#BD93F9",
    startBg: "#00000000", startForeground: "#BD93F9", accent: "#BD93F9", menuBg: "#FA282A36", menuBorder: "#44475A", menuHover: "#44475A",
    inputBg: "#21222C", inputBorder: "#6272A4", logo: ["#FF5555","#50FA7B","#8BE9FD","#F1FA8C"], buttonRadius: "8" },

  { file: "Nord", name: "Nord",
    bg: "#F22E3440", border: "#3B4252", fg: "#ECEFF4", muted: "#B8C0D0",
    hover: "#3B4252", pressed: "#434C5E", active: "#434C5E", indicator: "#88C0D0",
    startBg: "#00000000", startForeground: "#88C0D0", accent: "#88C0D0", menuBg: "#FA2E3440", menuBorder: "#4C566A", menuHover: "#3B4252",
    inputBg: "#3B4252", inputBorder: "#4C566A", logo: ["#BF616A","#A3BE8C","#81A1C1","#EBCB8B"] },

  { file: "Cyberpunk", name: "Cyberpunk",
    bg: "#F20A0A12", border: "#FF00F0", fg: "#00F0FF", muted: "#8A8AB0",
    hover: "#3300F0FF", pressed: "#5500F0FF", active: "#3300F0FF", indicator: "#FF00F0",
    startBg: "#00000000", startForeground: "#FFE600", accent: "#FF00F0", menuBg: "#FA0A0A12", menuBorder: "#00F0FF", menuHover: "#3300F0FF",
    inputBg: "#12121F", inputBorder: "#FF00F0", logo: ["#FF00F0","#00F0FF","#FFE600","#00FF9F"],
    font: "Bahnschrift, Segoe UI", buttonRadius: "0", menuRadius: "0", indicatorHeight: "2", popupShadow: "#80FF00F0" },

  { file: "Catppuccin", name: "Catppuccin",
    bg: "#F21E1E2E", border: "#313244", fg: "#CDD6F4", muted: "#A6ADC8",
    hover: "#313244", pressed: "#45475A", active: "#313244", indicator: "#CBA6F7",
    startBg: "#00000000", startForeground: "#CBA6F7", accent: "#CBA6F7", menuBg: "#FA1E1E2E", menuBorder: "#45475A", menuHover: "#313244",
    inputBg: "#181825", inputBorder: "#45475A", logo: ["#F38BA8","#A6E3A1","#89B4FA","#F9E2AF"], buttonRadius: "10", menuRadius: "12" },

  { file: "Solarized", name: "Solarized",
    bg: "#F2002B36", border: "#073642", fg: "#EEE8D5", muted: "#93A1A1",
    hover: "#073642", pressed: "#586E75", active: "#073642", indicator: "#B58900",
    startBg: "#00000000", startForeground: "#CB4B16", accent: "#CB4B16", menuBg: "#FA002B36", menuBorder: "#586E75", menuHover: "#073642",
    inputBg: "#073642", inputBorder: "#586E75", logo: ["#DC322F","#859900","#268BD2","#B58900"], buttonRadius: "4" },

  { file: "AmberTerminal", name: "Amber Terminal",
    bg: "#FA0A0600", border: "#FFB000", fg: "#FFB000", muted: "#B37B00",
    hover: "#33FFB000", pressed: "#55FFB000", active: "#33FFB000", indicator: "#FFB000",
    startBg: "#00000000", accent: "#FFB000", menuBg: "#FA0A0600", menuBorder: "#FFB000", menuHover: "#33FFB000",
    inputBg: "#140C00", inputBorder: "#FFB000", logo: ["#FFB000","#FFB000","#FFB000","#FFB000"],
    font: "Consolas", startText: "START", buttonRadius: "0", menuRadius: "0", indicatorHeight: "2", startMinWidth: "80",
    buttonBorder: "#60FFB000", buttonBorderThickness: "1", popupShadow: "#60FFB000" },

  { file: "RosePine", name: "Rose Pine",
    bg: "#F2191724", border: "#26233A", fg: "#E0DEF4", muted: "#908CAA",
    hover: "#26233A", pressed: "#403D52", active: "#26233A", indicator: "#EBBCBA",
    startBg: "#00000000", startForeground: "#C4A7E7", accent: "#C4A7E7", menuBg: "#FA191724", menuBorder: "#403D52", menuHover: "#26233A",
    inputBg: "#1F1D2E", inputBorder: "#403D52", logo: ["#EB6F92","#9CCFD8","#31748F","#F6C177"], buttonRadius: "8" },

  { file: "Gruvbox", name: "Gruvbox",
    bg: "#F2282828", border: "#3C3836", fg: "#EBDBB2", muted: "#A89984",
    hover: "#3C3836", pressed: "#504945", active: "#3C3836", indicator: "#FE8019",
    startBg: "#00000000", startForeground: "#FABD2F", accent: "#FABD2F", menuBg: "#FA282828", menuBorder: "#504945", menuHover: "#3C3836",
    inputBg: "#1D2021", inputBorder: "#504945", logo: ["#FB4934","#B8BB26","#83A598","#FABD2F"], buttonRadius: "4" },
];

const solid = (key, color) => `    <SolidColorBrush x:Key="${key}" Color="${color}" />`;
const vgrad = (key, stops) => `    <LinearGradientBrush x:Key="${key}" StartPoint="0,0" EndPoint="0,1">
${stops.map((c, i) => `        <GradientStop Color="${c}" Offset="${(i / (stops.length - 1)).toFixed(2)}" />`).join("\n")}
    </LinearGradientBrush>`;
const radial = (key, stops) => `    <RadialGradientBrush x:Key="${key}" GradientOrigin="0.5,0.25" Center="0.5,0.5" RadiusX="0.7" RadiusY="0.7">
${stops.map((c, i) => `        <GradientStop Color="${c}" Offset="${(i / (stops.length - 1)).toFixed(2)}" />`).join("\n")}
    </RadialGradientBrush>`;

for (const raw of themes) {
  const t = { ...base, ...raw };
  const lines = [
    `<!-- Theme: ${t.name} (generated by gen-themes.mjs — edit the script, not this file) -->`,
    `<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"`,
    `                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"`,
    `                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">`,
    ``,
    `    <sys:String x:Key="ThemeName">${t.name}</sys:String>`,
    `    <sys:String x:Key="LogoStyle">${t.logoStyle}</sys:String>`,
    // System.String has no parameterless ctor, so an empty <sys:String/> blows up at load time → use x:Static for "".
    t.startText ? `    <sys:String x:Key="StartText">${t.startText}</sys:String>`
                : `    <x:Static x:Key="StartText" Member="sys:String.Empty" />`,
    `    <FontFamily x:Key="ThemeFont">${t.font}</FontFamily>`,
    ``,
    t.bgGradient ? vgrad("TaskbarBackground", t.bgGradient) : solid("TaskbarBackground", t.bg),
    solid("TaskbarBorder", t.border),
    solid("TaskbarForeground", t.fg),
    solid("TaskbarForegroundMuted", t.muted),
    ``,
    solid("ButtonHover", t.hover),
    solid("ButtonPressed", t.pressed),
    solid("ButtonActive", t.active),
    solid("ButtonActiveIndicator", t.indicator),
    solid("ButtonBorder", t.buttonBorder),
    ``,
    t.startRadial ? radial("StartBackground", t.startRadial)
      : t.startGradient ? vgrad("StartBackground", t.startGradient)
      : solid("StartBackground", t.startBg),
    solid("StartHover", t.startHover ?? t.hover),
    solid("StartForeground", t.startForeground ?? t.fg),
    solid("Accent", t.accent),
    ``,
    solid("MenuBackground", t.menuBg),
    solid("MenuBorder", t.menuBorder),
    solid("MenuForeground", t.menuForeground ?? t.fg),
    solid("MenuItemHover", t.menuHover),
    solid("MenuItemHoverForeground", t.menuItemHoverForeground ?? t.menuForeground ?? t.fg),
    solid("InputBackground", t.inputBg),
    solid("InputBorder", t.inputBorder),
    ``,
    solid("LogoBrush1", t.logo[0]),
    solid("LogoBrush2", t.logo[1]),
    solid("LogoBrush3", t.logo[2]),
    solid("LogoBrush4", t.logo[3]),
    `    <Color x:Key="PopupShadow">${t.popupShadow}</Color>`,
    ``,
    `    <CornerRadius x:Key="ButtonCornerRadius">${t.buttonRadius}</CornerRadius>`,
    `    <CornerRadius x:Key="StartCornerRadius">${t.startRadius ?? t.buttonRadius}</CornerRadius>`,
    `    <CornerRadius x:Key="TaskbarCornerRadius">${t.taskbarRadius}</CornerRadius>`,
    `    <CornerRadius x:Key="MenuCornerRadius">${t.menuRadius}</CornerRadius>`,
    `    <Thickness x:Key="TaskbarBorderThickness">${t.taskbarBorderThickness}</Thickness>`,
    `    <Thickness x:Key="ButtonBorderThickness">${t.buttonBorderThickness}</Thickness>`,
    `    <sys:Double x:Key="ActiveIndicatorHeight">${t.indicatorHeight}</sys:Double>`,
    `    <sys:Double x:Key="StartButtonMinWidth">${t.startMinWidth}</sys:Double>`,
    ``,
    `</ResourceDictionary>`,
    ``,
  ];
  writeFileSync(join(here, `${t.file}.xaml`), lines.join("\n"), "utf8");
  console.log("wrote", `${t.file}.xaml`);
}
