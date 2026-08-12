---
description: Sıcak yola (chunker, crypto, pack okuma/yazma, IO kısıtlama) dokunan bir değişikliği Opus/xhigh ile yap
model: opus
---

$ARGUMENTS

Bu iş CLAUDE.md kural 4'ün (sıcak yolda allocation yok, ArrayPool<byte>.Shared
kullan) ve muhtemelen kural 5'in (yaz-sonra-sil) kapsamına giriyor. Bir
kısıtlama/throughput değişikliğiyse, kısıtın motorun hangi ucuna (kaynak
okuma mı, depo yazma mı) konduğunun sonucu belirlediğini unutma — "kısıtlı
kısıtsızdan yavaş" diyen bir test yeşil yanar ama hedef throughput'u
doğrulamaz; gerçek bir ölçüm (stopwatch + toleranslı aralık) yaz.
